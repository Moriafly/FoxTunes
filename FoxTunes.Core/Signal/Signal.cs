﻿using FoxTunes.Interfaces;

namespace FoxTunes
{
    public class Signal : ISignal
    {
        public Signal(object source, string name, object state = null)
        {
            this.Source = source;
            this.Name = name;
            this.State = state;
        }

        public object Source { get; private set; }

        public string Name { get; private set; }

        public object State { get; private set; }
    }
}
