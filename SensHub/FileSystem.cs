﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sensaura.Utilities;

namespace SensHub
{
	internal class FolderImpl : IFolder
	{
		private string m_path;

		internal FolderImpl(string path)
		{
			m_path = path;
		}

		public Stream CreateFile(string name, Sensaura.Utilities.FileAccess access, CreationOptions options)
		{
			throw new NotImplementedException();
		}

		public bool FileExists(string name)
		{
			throw new NotImplementedException();
		}
	}


	public class FileSystem : IFileSystem
	{
		// Base path and custom locations
		private string m_basePath;
		private Dictionary<string, string> m_custom;
		private Dictionary<string, IFolder> m_folders;

		public FileSystem(string basePath)
		{
			// Make sure the base path exists and is a directory
			if (!Directory.Exists(basePath))
				Directory.CreateDirectory(basePath);
			// Save state
			m_basePath = basePath;
			m_custom = new Dictionary<string, string>();
		}

		/// <summary>
		/// Map a physical path to a subdirectory in the virtual file system.
		/// </summary>
		/// <param name="name"></param>
		/// <param name="path"></param>
		public void SetPath(string name, string path)
		{
			// Check parameters
			if (!name.IsValidIdentifier())
				throw new ArgumentException("Invalid directory name");
			// Make sure the target mapping exists
			if (!Directory.Exists(path))
				Directory.CreateDirectory(path);
			// Add it to the list
			m_custom.Add(name, path);
		}

		/// <summary>
		/// Get a system path from the child path name. If a mapping has
		/// been registered that location will be used, otherwise the child
		/// directory will be created under the base path location.
		/// </summary>
		/// <param name="name"></param>
		/// <returns></returns>
		public string GetPath(string name)
		{
			// Check parameters
			if (!name.IsValidIdentifier())
				throw new ArgumentException("Invalid directory name");
			// Do we have a custom mapping ?
			if (m_custom.ContainsKey(name))
				return m_custom[name];
			// Generate from base path
			string target = Path.Combine(m_basePath, name);
			Directory.CreateDirectory(target);
			return target;
		}

		/// <summary>
		/// Open a folder in the virtual storage area
		/// </summary>
		/// <param name="name"></param>
		/// <returns></returns>
		public IFolder OpenFolder(string name)
		{
			// Create a new IFolder if we don't have one
			if (!m_folders.ContainsKey(name))
				m_folders[name] = new FolderImpl(GetPath(name));
			return m_folders[name];
		}
	}
}
