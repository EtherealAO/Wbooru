﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Wbooru
{
    public static class Container
    {
        private static CompositionContainer instance = null;

        public static CompositionContainer Default
        {
            get
            {
                if (instance != null)
                    return instance;

                throw new Exception("MEF hasn't been initalized yet.");
            }
        }

        public static void BuildDefault()
        {
            if (!Directory.Exists("Plugins"))
                Directory.CreateDirectory("Plugins");

            var catalog = new AggregateCatalog(
                new DirectoryCatalog("Plugins"),
                new DirectoryCatalog("."),
                new AssemblyCatalog(typeof(Container).Assembly)
                );

            instance = new CompositionContainer(catalog);
        }
    }
}
