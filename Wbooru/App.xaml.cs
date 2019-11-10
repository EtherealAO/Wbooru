﻿using System.ComponentModel.Composition;
using System.Windows;
using Wbooru.Kernel;
using System.Linq;
using Wbooru.Settings;
using System.Collections.Generic;
using Wbooru.Galleries;
using System;
using Wbooru.Utils.Resource;

namespace Wbooru
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            Init();
        }

        internal static void Init()
        {
            Container.BuildDefault();

            DownloadManager.Init();

            SchedulerManager.Init();

            TagManager.InitTagManager();

            ImageResourceManager.InitImageResourceManager();
        }

        internal static void Term()
        {
            DownloadManager.Close();
            SettingManager.SaveSettingFile();
            SchedulerManager.Term();
        }
    }
}
