﻿using LambdaConverters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace Wbooru.UI.ValueConverters
{
    public static class SimpleExpressionConverters
    {
        public static IValueConverter ReverseBooleanToVisibilityConverter => ValueConverter.Create<bool?, Visibility>(b => !(b.Value ?? false) ? Visibility.Visible : Visibility.Hidden);
    }
}