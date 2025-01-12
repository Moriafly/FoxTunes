﻿using System;

namespace FoxTunes
{
    public class UIComponent
    {
        public const string PLACEHOLDER = "00000000-0000-0000-0000-000000000000";

        public UIComponent(UIComponentAttribute attribute, Type type)
        {
            this.Id = attribute.Id;
            this.Name = StringResourceReader.GetString(type, nameof(this.Name)) ?? type.Name;
            this.Description = StringResourceReader.GetString(type, nameof(this.Description)) ?? string.Empty;
            this.Role = attribute.Role;
            this.Type = type;
        }

        public string Id { get; private set; }

        public string Name { get; private set; }

        public string Description { get; private set; }

        public UIComponentRole Role { get; private set; }

        public Type Type { get; private set; }
    }
}
