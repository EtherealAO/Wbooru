﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wbooru.Models.Gallery;

namespace Wbooru.Utils
{
    public static class FileNameHelper
    {
        const char REPLACE_VAILD_CHAR = '_';

        public static string FilterFileName(string file_name)
        {
            var result = file_name;

            foreach (var ch in Path.GetInvalidFileNameChars())
                result = result.Replace(ch, REPLACE_VAILD_CHAR);

            Log.Debug($"{file_name} -> {result}");

            return result;
        }

        public static string GetFileNameWithoutExtName(GalleryImageDetail detail)
        {
            return FilterFileName($"{detail.ID} {string.Join(" ",detail.Tags)}");
        }
    }
}