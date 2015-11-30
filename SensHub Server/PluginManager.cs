﻿using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SensHub.Plugins;
using Splat;

namespace SensHub.Server
{
	/// <summary>
	/// This class manages the plugins.
	/// </summary>
	internal class PluginManager : IEnableLogger
	{
		private Dictionary<Guid, IPlugin> m_pluginsAvailable = new Dictionary<Guid, IPlugin>();
		private Dictionary<Guid, IPluginHost> m_pluginsEnabled = new Dictionary<Guid, IPluginHost>();

		/// <summary>
		/// Add an individual to the master list.
		/// </summary>
		/// <param name="plugin">The plugin instance to add.</param>
		public bool AddPlugin(IPlugin plugin)
		{
			lock (m_pluginsAvailable)
			{
				if (m_pluginsAvailable.ContainsKey(plugin.UUID))
				{
					this.Log().Warn("Plugin with UUID '{0}' is already registered.", plugin.UUID);
					return false;
				}
				m_pluginsAvailable[plugin.UUID] = plugin;
			}
			return true;
		}

		/// <summary>
		/// Populate the plugin list with all plugins found in the specified directory.
		/// 
		/// This method does not initialise the plugins, it simply discovers them and
		/// adds them to master list.
		/// </summary>
		/// <param name="directory"></param>
		public void LoadPlugins(string directory)
		{
			this.Log().Debug("Scanning directory '{0}' for plugins.", directory);
			if (!Directory.Exists(directory))
			{
				this.Log().Warn("Plugin directory '{0}' does not exist.", directory);
				return;
			}
			String[] files = Directory.GetFiles(directory, "*.dll");
			foreach (string pluginDLL in files)
			{
				// Load the assembly
				Assembly asm = null;
				try
				{
					this.Log().Debug("Attempting to load '{0}'", pluginDLL);
					asm = Assembly.LoadFile(pluginDLL);
				}
				catch (Exception ex)
				{
					this.Log().Error("Failed to load assembly from file '{0}' - {1}", pluginDLL, ex.ToString());
					continue;
				}
				// Get the plugins defined in the file (it can have more than one)
				Type[] types = asm.GetTypes();
				foreach (var candidate in types)
				{
					if (typeof(IPlugin).IsAssignableFrom(candidate))
					{
						try
						{
							this.Log().Debug("Creating plugin '{0}.{1}'", candidate.Namespace, candidate.Name);
							Object instance = Activator.CreateInstance(candidate);
							AddPlugin((IPlugin)instance);
						}
						catch (Exception ex)
						{
							this.Log().Error("Unable to create plugin with class '{0}' in extension '{1}' - {2}",
								candidate.Name,
								pluginDLL,
								ex.ToString()
								);
							continue;
						}
					}
				}
			}
		}

		/// <summary>
		/// Initialise all the plugins.
		/// 
		/// Every available plugin (added with <see cref="AddPlugin"/> or <see cref="LoadPlugins"/>)
		/// that is not marked as disabled will be initialised.
		/// </summary>
		public void InitialisePlugins()
		{
			lock(m_pluginsAvailable) 
			{
				lock(m_pluginsEnabled) 
				{
					foreach (Guid uuid in m_pluginsAvailable.Keys)
					{
						// TODO: Make sure the plugin is enabled
						// Create the IPluginHost for this plugin and enable it
						PluginHost host = new PluginHost(m_pluginsAvailable[uuid]);
						if (host.EnablePlugin())
						{
							m_pluginsEnabled[uuid] = host;
						}
					}
				}
			}
		}
	}
}