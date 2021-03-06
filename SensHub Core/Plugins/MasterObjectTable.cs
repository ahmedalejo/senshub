﻿using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SensHub.Plugins;
using SensHub.Core.Utils;
using Splat;

namespace SensHub.Core.Plugins
{
    /// <summary>
    /// This class manages all the IUserObject instances in the system
	/// 
	/// The master object table maintains information about all IUserObject
	/// instances that are active in the running system. It provides a
	/// single source for all information about these objects - descriptions,
	/// configuration information and configuration data.
    /// </summary>
    public class MasterObjectTable : IPackable, IEnableLogger
    {
		//--- Instance variables
		private Dictionary<Guid, IUserObject> m_instances;
		private Dictionary<string, IObjectDescription> m_descriptions = new Dictionary<string, IObjectDescription>();
		private Dictionary<string, IConfigurationDescription> m_configinfo = new Dictionary<string, IConfigurationDescription>();
		private IFolder m_configDirectory;
        public List<Assembly> Assemblies { get; private set; }

		/// <summary>
		/// Constructor
		/// </summary>
        public MasterObjectTable()
        {
			m_instances = new Dictionary<Guid, IUserObject>();
			m_descriptions = new Dictionary<string, IObjectDescription>();
			m_configinfo = new Dictionary<string, IConfigurationDescription>();
            Assemblies = new List<Assembly>();
			// Get the containing folder for configurations
			IFolder fs = Locator.Current.GetService<IFolder>();
			m_configDirectory = fs.OpenFolder(ServiceManager.DataFolder);
        }

        /// <summary>
        /// Pack the table in a form suitable for RPC
        /// </summary>
        /// <returns></returns>
        public IDictionary<string, object> Pack()
        {
			Dictionary<string, object> mot = new Dictionary<string, object>();
			// Add entries for all types
			foreach (string type in Enum.GetNames(typeof(UserObjectType)))
				mot[type] = new Dictionary<string, object>();
			// Now add all the instances
			lock (m_instances)
			{
				foreach (IUserObject instance in m_instances.Values)
				{
					// We just want the description for each object
					IDictionary<string, object> detail = GetDescription(instance.UUID).Pack();
					// For IUserCreatableObjects we add the parent as well
					IUserCreatableObject creatable = instance as IUserCreatableObject;
					if (creatable != null)
						detail["ParentUUID"] = creatable.ParentUUID.ToString();
					// Add it to the appropriate spot
					Dictionary<string, object> container = mot[instance.ObjectType.ToString()] as Dictionary<string, object>;
					container[instance.UUID.ToString()] = detail;
				}
			}
			// All done
			return mot;
		}

		#region Internal Implementation
		/// <summary>
		/// Get information about an object.
		/// </summary>
		/// <param name="forInstance"></param>
		/// <param name="instance"></param>
		/// <param name="description"></param>
		/// <returns></returns>
		private bool GetObjectInformation(Guid forInstance, out IUserObject instance, out IConfigurationDescription description)
		{
			description = null;
			instance = GetInstance(forInstance);
			if (instance == null)
			{
				this.Log().Warn("Requested configuration for non-existant object '{0}'", forInstance);
				return false;
			}
			IConfigurable configurable = instance as IConfigurable;
			if (configurable == null)
			{
				this.Log().Warn("Requested configuration for unconfigurable object '{0}' (Class {1}.{2})", forInstance, instance.GetType().Namespace, instance.GetType().Name);
				return false;
			}
			description = GetConfigurationDescription(forInstance);
			if (description == null)
			{
				this.Log().Warn("No configuration description for object '{0}' (Class {1}.{2})", forInstance, instance.GetType().Namespace, instance.GetType().Name);
				return false;
			}
			return true;
		}
		#endregion

		#region Initial Setup
		/// <summary>
		/// Add an object instance to the master table
		/// </summary>
		/// <param name="instance"></param>
		public bool AddInstance(IUserObject instance)
		{
			lock (m_instances)
			{
				if (m_instances.ContainsKey(instance.UUID))
				{
					if (m_instances[instance.UUID] != instance)
					{
						this.Log().Warn("Object '{0}' is already registered with a different instance.", instance.UUID);
						return false;
					}
					return true;
				}
				// Add and test for supporting metadata
				m_instances[instance.UUID] = instance;
				// TODO: Make sure we have a description and a configuration for the instance
				if (GetDescription(instance.UUID) != null)
				{
                    // Does this instance have configuration information?
                    if ((instance as IConfigurable) != null)
                    {
                        if (GetConfigurationDescription(instance.UUID) == null)
                            this.Log().Warn("Object '{0}' has no configuration information. Will not add.", instance.UUID);
                        else
                            return true;
                    }
                    else
                        return true;
				}
				else
					this.Log().Warn("Object '{0}' has no description information. Will not add.", instance.UUID);
				// Not enough data, remove it
				m_instances.Remove(instance.UUID);
				return false;
			}
		}

		/// <summary>
		/// Remove an instance from the master table.
		/// </summary>
		/// <param name="uuid"></param>
		public bool RemoveInstance(Guid uuid)
		{
			lock (m_instances)
			{
				if (!m_instances.ContainsKey(uuid))
					return false;
				// Get the instance and see what other steps are needed
				IUserObject instance = m_instances[uuid];
				if (!instance.ObjectType.IsDeletable())
					return false;
				// TODO: Remove the configuration
				// TODO: Remove the description
				// Finally we need to remove the instance
				m_instances.Remove(uuid);
				return true;
			}
		}

		/// <summary>
		/// Add a description for a given class name (or ID)
		/// </summary>
		/// <param name="clsName"></param>
		/// <param name="description"></param>
		internal void AddDescription(string clsName, IObjectDescription description)
		{
			lock (m_descriptions)
			{
				if (m_descriptions.ContainsKey(clsName))
					this.Log().Warn("Description already registered for class '{0}'", clsName);
				else
					m_descriptions[clsName] = description;
			}
		}

		/// <summary>
		/// Add a configuration description for a given class name
		/// </summary>
		/// <param name="clsName"></param>
		/// <param name="configuration"></param>
		internal void AddConfigurationDescription(string clsName, IConfigurationDescription configuration)
		{
			lock (m_configinfo)
			{
				if (m_configinfo.ContainsKey(clsName))
					this.Log().Warn("Configuration information already registered for class '{0}'", clsName);
				else
					m_configinfo[clsName] = configuration;
			}
		}

		/// <summary>
		/// Add all the metadata (descriptions and configuration descriptions)
		/// from an assembly.
		/// </summary>
		/// <param name="assembly"></param>
		public void AddMetaData(Assembly assembly)
		{
            Assemblies.Add(assembly);
			Stream source = assembly.GetManifestResourceStream(assembly.GetName().Name + ".Resources.metadata.xml");
			if (source == null)
			{
				LogHost.Default.Warn("Could not find metadata resource for assembly {0}.", assembly.GetName().Name);
				return;
			}
			MetadataParser.LoadFromStream(assembly, source);
		}

		/// <summary>
		/// Get the configuration for the instance
		/// 
		/// This version of the method loads directly from a named file (the file must
		/// still be in the 'configs' folder.
		/// </summary>
		/// <param name="forInstance"></param>
		/// <param name="filename"></param>
		/// <returns></returns>
		public IDictionary<string, object> GetConfigurationFromFile(Guid forInstance, string filename)
		{
			IUserObject instance;
			IConfigurationDescription description;
			if (!GetObjectInformation(forInstance, out instance, out description))
				return null;
			// Load the configuration values from the file (if it exists)
			IDictionary<string, object> values = null;
			if (m_configDirectory.FileExists(filename)) 
			{
				Stream json = m_configDirectory.CreateFile(filename, FileAccessMode.Read, CreationOptions.OpenIfExists);
				values = ObjectPacker.UnpackRaw(json);
				json.Dispose();
			}
			else
				values = new Dictionary<string, object>();
			return description.Verify(values);
		}
		#endregion

		#region General Access
		/// <summary>
		/// Get an object instance given a UUID
		/// </summary>
		/// <param name="forInstance"></param>
		/// <returns></returns>
		public IUserObject GetInstance(Guid forInstance)
		{
			lock (m_instances)
			{
				if (!m_instances.ContainsKey(forInstance))
					return null;
				return m_instances[forInstance];
			}
		}

		/// <summary>
		/// Get an object instance given a UUID
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="forInstance"></param>
		/// <returns></returns>
		public T GetInstance<T>(Guid forInstance) where T : IUserObject
		{
			IUserObject instance = GetInstance(forInstance);
			if (instance is T)
				return (T)instance;
			return default(T);
		}

		/// <summary>
		/// Get the description for the instance
		/// </summary>
		/// <param name="forInstance"></param>
		/// <returns></returns>
		public IObjectDescription GetDescription(Guid forInstance)
		{
			IUserObject instance = GetInstance(forInstance);
			if (instance == null)
				return null;
			lock (m_descriptions)
			{
				if (instance is IUserCreatableObject)
				{
					if (!m_descriptions.ContainsKey(instance.UUID.ToString()))
					{
						this.Log().Warn("Object '{0}' exists but has no description.", instance.UUID);
						return null;
					}
					return m_descriptions[instance.UUID.ToString()];
				}
				// Use the fully qualified class name to get the description
				string fqcn = instance.GetType().Namespace + "." + instance.GetType().Name;
				if (!m_descriptions.ContainsKey(fqcn))
				{
					this.Log().Warn("Expected to find description for class '{0}'.", fqcn);
					return null;
				}
				return m_descriptions[fqcn];
			}
		}

		/// <summary>
		/// Get the configuration for the instance
		/// </summary>
		/// <param name="forInstance"></param>
		/// <returns></returns>
		public IDictionary<string, object> GetConfiguration(Guid forInstance)
		{
			// TODO: Currently loading from file, should be stored in DB
			return GetConfigurationFromFile(forInstance, string.Format("{0}.json", forInstance));
		}

		/// <summary>
		/// Get the configuration description for the instance
		/// </summary>
		/// <param name="forInstance"></param>
		/// <returns></returns>
		public IConfigurationDescription GetConfigurationDescription(Guid forInstance)
		{
			IUserObject instance = GetInstance(forInstance);
			if (instance == null)
				return null;
			// User creatable object use the parent to provide the description
			if (instance is IUserCreatableObject)
				return GetConfigurationDescription((instance as IUserCreatableObject).ParentUUID);
			// Look it up
			lock (m_configinfo)
			{
				// Use the fully qualified class name to get the config info
				string fqcn = instance.GetType().Namespace + "." + instance.GetType().Name;
				if (!m_configinfo.ContainsKey(fqcn))
				{
					this.Log().Warn("Could not find configuration information for class '{0}'", fqcn);
					return null;
				}
				return m_configinfo[fqcn];
			}
		}
		#endregion
	}
}
