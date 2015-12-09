﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SensHub.Plugins
{
    /// <summary>
    /// The types of objects we support
    /// </summary>
    public enum UserObjectType
    {
        Plugin,
        Trigger,
        TriggerFactory,
        Filter,
        FilterFactory,
        Action,
        ActionFactory,
        Source,
        SourceFactory
    }

    /// <summary>
    /// Base interface for objects that are displayed to the user.
    /// 
    /// Each of these objects are represented by a UUID.
    /// </summary>
    public interface IUserObject : IDescribed
    {
        /// <summary>
        /// The unique identifier for this object
        /// </summary>
        Guid UUID { get; }

        /// <summary>
        /// The type of the user object.
        /// </summary>
        UserObjectType ObjectType { get; }
    }

    /// <summary>
    /// Base interface for user creatable objects.
    /// 
    /// These are a special class of user object that can be created
    /// by the user. The UUID in this case is assigned by the system
    /// and the object keeps a reference to it's parent object.
    /// </summary>
    public interface IUserCreatableObject : IUserObject, IConfigurable
    {
        /// <summary>
        /// The identifer of the parent for this object.
        /// </summary>
        Guid ParentUUID { get; }
    }

    /// <summary>
    /// A factory for user creatable objects
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IUserObjectFactory<T> : IUserObject, IConfigurable where T : IUserCreatableObject
    {
        /// <summary>
        /// Create or reinstate an object given an ID and a configuration.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="config"></param>
        /// <returns></returns>
        T CreateInstance(Guid id, Configuration config);
    }
}
