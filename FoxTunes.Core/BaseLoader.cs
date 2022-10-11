﻿using FoxTunes.Interfaces;
using System.Collections.Generic;
using System.Linq;
using System;

namespace FoxTunes
{
    public abstract class BaseLoader<T> : BaseComponent, IBaseLoader<T> where T : IBaseComponent
    {
        public virtual IEnumerable<T> Load(ICore core)
        {
            var components = new List<T>();
            foreach (var type in ComponentScanner.Instance.GetComponents(typeof(T)))
            {
                if (!this.IncludeType(type))
                {
                    continue;
                }
                var component = default(T);
                try
                {
                    component = ComponentActivator.Instance.Activate<T>(type);
                }
                catch (Exception e)
                {
                    Logger.Write(this, LogLevel.Warn, "Failed to activate component: {0} => {1}: {2}", type.Name, typeof(T).Name, e.Message);
                    continue;
                }
                Logger.Write(this, LogLevel.Debug, "Activated component: {0} => {1}", type.Name, typeof(T).Name);
                components.Add(component);
            }
            return components.OrderBy(this.ComponentPriority);
        }

        public virtual bool IncludeType(Type type)
        {
            {
                var dependencies = default(IEnumerable<PlatformDependencyAttribute>);
                if (type.HasCustomAttributes<PlatformDependencyAttribute>(out dependencies))
                {
                    foreach (var dependency in dependencies)
                    {
                        var version = new Version(dependency.Major, dependency.Minor);
                        if (Environment.OSVersion.Version < version)
                        {
                            Logger.Write(this, LogLevel.Debug, "Not loading component \"{0}\": Requires platform {1}.{2}.", type.FullName, dependency.Major, dependency.Minor);
                            return false;
                        }
                        if (dependency.Architecture != ProcessorArchitecture.None)
                        {
                            var is64BitProcess = Environment.Is64BitProcess;
                            var is34BitProcess = !is64BitProcess;
                            if (dependency.Architecture == ProcessorArchitecture.X86 && is64BitProcess)
                            {
                                Logger.Write(this, LogLevel.Debug, "Not loading component \"{0}\": Requires platform X86.", type.FullName);
                                return false;
                            }
                            if (dependency.Architecture == ProcessorArchitecture.X64 && !is34BitProcess)
                            {
                                Logger.Write(this, LogLevel.Debug, "Not loading component \"{0}\": Requires platform X64.", type.FullName);
                                return false;
                            }
                        }
                    }
                }
            }
            {
                var dependencies = default(IEnumerable<ComponentDependencyAttribute>);
                if (type.HasCustomAttributes<ComponentDependencyAttribute>(out dependencies))
                {
                    foreach (var dependency in dependencies)
                    {
                        if (!string.IsNullOrEmpty(dependency.Slot))
                        {
                            var id = ComponentResolver.Instance.Get(dependency.Slot);
                            if (!string.IsNullOrEmpty(dependency.Id))
                            {
                                if (string.Equals(id, ComponentSlots.None, StringComparison.OrdinalIgnoreCase))
                                {
                                    Logger.Write(this, LogLevel.Warn, "Component \"{0}\" depends on \"{1}\" but the slot is not configured, it will be loaded anyway.", type.FullName, dependency.Id);
                                }
                                else if (!string.Equals(id, dependency.Id, StringComparison.OrdinalIgnoreCase))
                                {
                                    Logger.Write(this, LogLevel.Debug, "Not loading component \"{0}\": Missing dependency \"{1}\".", type.FullName, dependency.Id);
                                    return false;
                                }
                            }
                            else if (string.Equals(id, ComponentSlots.Blocked, StringComparison.OrdinalIgnoreCase))
                            {
                                Logger.Write(this, LogLevel.Debug, "Not loading component \"{0}\": Missing dependency \"{1}\".", type.FullName, dependency.Slot);
                                return false;
                            }
                        }
                    }
                }
            }
            var component = default(ComponentAttribute);
            if (!type.HasCustomAttribute<ComponentAttribute>(out component))
            {
                return true;
            }
            if (component == null)
            {
                return true;
            }
            {
                var id = ComponentResolver.Instance.Get(component.Slot);
                if (string.Equals(id, ComponentSlots.None, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                if (string.Equals(id, component.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            Logger.Write(this, LogLevel.Debug, "Not loading component \"{0}\": Blocked by configuration.", type.FullName);
            return false;
        }

        public Func<T, byte> ComponentPriority
        {
            get
            {
                return component =>
                {
                    var attribute = component.GetType().GetCustomAttribute<ComponentAttribute>();
                    if (attribute == null)
                    {
                        return ComponentAttribute.PRIORITY_NORMAL;
                    }
                    return attribute.Priority;
                };
            }
        }
    }
}
