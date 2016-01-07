﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IotWeb.Common;
using IotWeb.Common.Util;
using SensHub.Plugins;
using SensHub.Core.Plugins;
using Splat;

namespace SensHub.Core
{
	public class ServiceManager : IUserObject, IConfigurable, IServer, IEnableLogger
	{
		// Top level file locations.
		public const string DataFolder = "data";
		public const string PluginFolder = "plugins";
		public const string SiteFolder = "site";
		public const string LogFolder = "logs";

		// Object identification
		private static Guid MyUUID = Guid.Parse("{377ECFA2-2B36-4BBF-8F3F-66C0582DFED8}");
		private const UserObjectType MyType = UserObjectType.Server;

		#region Properties and events
		/// <summary>
		/// Server stopped event handler
		/// </summary>
		public event ServerStoppedHandler ServerStopped;

		/// <summary>
		/// Determine if we are running or not
		/// </summary>
		public bool Running { get; private set; }

		/// <summary>
		/// The port to listen on for HTTP requests
		/// </summary>
		public int HttpPort { get; private set; }

		/// <summary>
		/// The current logging level
		/// </summary>
		public LogLevel LogLevel { get; private set; }
		#endregion

		#region Implementation of IUserObject
		public System.Guid UUID
		{
			get { return MyUUID; }
		}

		public UserObjectType ObjectType
		{
			get { return MyType; }
		}
		#endregion

		#region Implementation of IConfigurable
		public bool ValidateConfiguration(IConfigurationDescription description, IDictionary<string, object> values, IDictionary<string, string> failures)
		{
			// TODO: Implement this
			return true;
		}

		/// <summary>
		/// Apply the configuration values
		/// </summary>
		/// <param name="description"></param>
		/// <param name="values"></param>
		public void ApplyConfiguration(IConfigurationDescription description, IDictionary<string, object> values)
		{
			// Get the logging level
			LogLevel logLevel;
			if (!Enum.TryParse<LogLevel>(description.GetAppliedValue(values, "logLevel").ToString(), out logLevel))
				logLevel = LogLevel.Warn;
			LogLevel = logLevel;
			// Get the HTTP port
			HttpPort = (int)description.GetAppliedValue(values, "httpPort");
		}
		#endregion

		#region Implementation of IService
		public void Start()
		{
			// TODO: Implement this
		}

		public void Stop()
		{
			// TODO: Implement this
		}
		#endregion

		/// <summary>
		/// Constructor
		/// </summary>
		public ServiceManager()
		{
			// Add ourselves to the MOT
			MasterObjectTable mot = Locator.Current.GetService<MasterObjectTable>();
			mot.AddMetaData(Utilities.GetContainingAssembly<ServiceManager>());
			mot.AddInstance(this);
			// Load and apply our configuration (IFolder must be available by now)
			IConfigurationDescription serverConfigDescription = mot.GetConfigurationDescription(UUID);
			IDictionary<string, object> serverConfig = mot.GetConfigurationFromFile(UUID, "SensHub.json");
			ApplyConfiguration(serverConfigDescription, serverConfig);
		}

		/// <summary>
		/// Add a new server to the collection.
		/// </summary>
		/// <param name="server"></param>
		public void AddServer(IServer server)
		{
			if (Running)
				throw new InvalidOperationException("Services have already been started.");
		}
	}
}
