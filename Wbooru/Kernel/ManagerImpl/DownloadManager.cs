﻿using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Wbooru.Models;
using Wbooru.Network;
using Wbooru.Persistence;
using Wbooru.UI.Controls;
using Wbooru.Utils;
using System.Net;
using Wbooru.Kernel.DI;
using System.Net.Http;

namespace Wbooru.Kernel.ManagerImpl
{
    [PriorityExport(typeof(IDownloadManager), Priority = 0)]
    internal class DownloadManager : IDownloadManager
    {
        private AbortableThread timer_thread;

        public ObservableCollection<DownloadWrapper> DownloadList { get; } = new ObservableCollection<DownloadWrapper>();

        private HashSet<DownloadWrapper> RunningDownloadTask = new HashSet<DownloadWrapper>();
        private Dictionary<DownloadWrapper, FileStream> FileStreamHolder = new Dictionary<DownloadWrapper, FileStream>();

        public Task OnExit()
        {
            try
            {
                timer_thread?.Abort();
            }
            catch { }

            return LocalDBContext.PostDbAction(async db =>
            {
                foreach (var item in DownloadList)
                {
                    if (item.Status == DownloadTaskStatus.Started)
                    {
                        Log.Debug($"Pause download task:{item.DownloadInfo.FileName}");
                        await DownloadPause(item);
                    }

                    if (db.Downloads.Find(item.DownloadInfo.DownloadId) is Download entity)
                    {
                        Log.Debug($"Update download record :{item.DownloadInfo.DownloadFullPath}");
                        db.Entry(entity).CurrentValues.SetValues(item.DownloadInfo);
                    }
                    else
                    {
                        Log.Debug($"Add download record :{item.DownloadInfo.DownloadFullPath}");
                        db.Downloads.Add(item.DownloadInfo);
                    }
                }

                db.SaveChanges();
                Log.Debug($"Download record save all done.");
            });
        }

        public Task DownloadRestart(DownloadWrapper download)
        {
            var temp_dl_path = download.DownloadInfo.DownloadFullPath + ".dl";
            File.Delete(temp_dl_path);

            download.Status = DownloadTaskStatus.Paused;
            DownloadStart(download);
            return Task.CompletedTask;
        }

        public Task OnInit()
        {
            //start a timer
            timer_thread = new AbortableThread(OnSpeedCalculationTimer);
            timer_thread.IsBackground = true;
            timer_thread.Name = "OnSpeedCalculationTimer";
            timer_thread.Start();

            return LocalDBContext.PostDbAction(db =>
            {
                try
                {
                    foreach (var item in db.Downloads.AsQueryable().Select(x => new DownloadWrapper()
                    {
                        DownloadInfo = x,
                        CurrentDownloadedLength = x.DisplayDownloadedLength,
                        Status = x.DisplayDownloadedLength != 0 && x.DisplayDownloadedLength == x.TotalBytes ? DownloadTaskStatus.Finished : DownloadTaskStatus.Paused
                    }))
                    {
                        Log.Debug($"Load download record :{item.DownloadInfo.DownloadFullPath}");
                        DownloadList.Add(item);
                    }
                }
                catch (Exception e)
                {
                    ExceptionHelper.DebugThrow(e);
                    Log.Error("Can't get download record from database.", e);
                }
            });
        }

        public async Task DownloadDelete(DownloadWrapper download)
        {
            await LocalDBContext.PostDbAction(db =>
            {
                DownloadPause(download);

                var temp_dl_path = download.DownloadInfo.DownloadFullPath + ".dl";
                File.Delete(temp_dl_path);
                DownloadList.Remove(download);

                if (download.IsSaveInDB && db.Downloads.Remove(download.DownloadInfo).Entity is Download)
                {
                    Log.Info("Deleted entity record in DB");
                    db.SaveChanges();
                }

                return Task.CompletedTask;
            });

            Log.Info($"Deleted download task :{download.DownloadInfo.FileName}");
        }

        public void OnSpeedCalculationTimer(CancellationToken cancellationToken)
        {
            var record = new Dictionary<DownloadWrapper, long>();

            while (!cancellationToken.IsCancellationRequested)
            {
                lock (RunningDownloadTask)
                {
                    foreach (var task in RunningDownloadTask)
                    {
                        if (!record.TryGetValue(task, out var prev_len))
                            prev_len = 0;
                        var current_len = task.CurrentDownloadedLength;
                        task.DownloadSpeed = current_len - prev_len;
                        record[task] = current_len;
                    }

                    RunningDownloadTask.RemoveWhere(x => x.Status != DownloadTaskStatus.Started && ((record[x] = 0) == 0));
                }

                Thread.Sleep(1000);
            }
        }

        public Task<bool> CheckIfContained(DownloadWrapper download)
        {
            //check
            if (DownloadList.FirstOrDefault(x => x.DownloadInfo == download.DownloadInfo && download.DownloadInfo.DownloadId < 0 && (x.DownloadInfo.DownloadId != download.DownloadInfo.DownloadId)) is DownloadWrapper _)
                return Task.FromResult(true);

            return Task.FromResult(false);
        }

        public Task DownloadStart(DownloadWrapper download)
        {
            if (download.Status == DownloadTaskStatus.Started || download.Status == DownloadTaskStatus.Finished)
            {
                Log.Info($"Download task {download.DownloadInfo.FileName} has already been started/finished.");
                return Task.CompletedTask;
            }

            //check
            if (string.IsNullOrWhiteSpace(download.DownloadInfo.DownloadFullPath))
            {
                var message = "此任务出现格式错误，没钦定下载路径";

                download.ErrorMessage = message;
                DownloadPause(download);

                throw new Exception(message);
            }

            if (!DownloadList.Contains(download))
                DownloadList.Add(download);

            download.ErrorMessage = string.Empty;

            var cancel_token_source = new CancellationTokenSource();
            download.CancelTokenSource = cancel_token_source;

            var task = Task.Run(() => OnDownloadTaskStart(download), cancel_token_source.Token);

            download.Status = DownloadTaskStatus.Started;

            lock (RunningDownloadTask)
            {
                RunningDownloadTask.Add(download);
            }

            Log.Info($"Started downloading task :{download.DownloadInfo.FileName}");
            return Task.CompletedTask;
        }

        public void OnDownloadTaskStart(DownloadWrapper download)
        {
            FileStream file_stream = null;

            try
            {
                var temp_dl_path = download.DownloadInfo.DownloadFullPath + ".dl";
                Directory.CreateDirectory(Path.GetDirectoryName(download.DownloadInfo.DownloadFullPath));
                file_stream = FileStreamHolder[download] = File.OpenWrite(temp_dl_path);
                download.CurrentDownloadedLength = file_stream.Length;
                file_stream.Seek(download.CurrentDownloadedLength, SeekOrigin.Begin);

                HttpResponseMessage response = null;

                try
                {
                    response = RequestHelper.CreateDeafult(download.DownloadInfo.DownloadUrl, request =>
                    {
                        if (download.CurrentDownloadedLength > 0)
                            request.Headers.Range.Ranges.Add(new System.Net.Http.Headers.RangeItemHeaderValue(download.CurrentDownloadedLength, request.Content.Headers.ContentLength ?? 0));
                    });
                }
                catch (Exception e) when (e.Message.Contains("416"))
                {
                    Log.Error("Redownload file because of HttpCode416...");

                    file_stream?.Dispose();
                    File.Delete(temp_dl_path);

                    file_stream = FileStreamHolder[download] = File.OpenWrite(temp_dl_path);
                    download.CurrentDownloadedLength = file_stream.Length;
                    file_stream.Seek(download.CurrentDownloadedLength, SeekOrigin.Begin);

                    response = RequestHelper.CreateDeafult(download.DownloadInfo.DownloadUrl);
                }

                using var response_stream = response.Content.ReadAsStream();

                var buffer = new byte[1024];
                int read_bytes = 0;

                do
                {
                    read_bytes = response_stream.Read(buffer, 0, buffer.Length);

                    if (download.Status != DownloadTaskStatus.Started || !file_stream.CanWrite)
                    {
                        Log.Debug($"Notice that download task {download.DownloadInfo.FileName} still continue to write when task status is not started.");
                        return;
                    }

                    file_stream.Write(buffer, 0, read_bytes);
                    file_stream.Flush();
                    download.CurrentDownloadedLength += read_bytes;
                } while (read_bytes != 0);

                //downloading finished
                file_stream.Dispose();

                if (File.Exists(download.DownloadInfo.DownloadFullPath))
                    File.Delete(download.DownloadInfo.DownloadFullPath);
                File.Move(temp_dl_path, download.DownloadInfo.DownloadFullPath);

                download.Status = DownloadTaskStatus.Finished;
                Log.Info($"Downloading task {download.DownloadInfo.FileName} now finished.");
            }
            catch (Exception e)
            {
                Log.Error(e.Message);
                ExceptionHelper.DebugThrow(e);
                //error ocured
                download.ErrorMessage = e.Message;
                DownloadPause(download);
                Toast.ShowMessage($"无法开始下载此图片:{e.Message}");
            }
            finally
            {
                file_stream?.Dispose();
            }
        }

        public Task DownloadPause(DownloadWrapper download)
        {
            if (download.Status == DownloadTaskStatus.Paused || download.Status == DownloadTaskStatus.Finished)
            {
                Log.Info($"Download task {download.DownloadInfo.FileName} has already been paused/finished.");
                return Task.CompletedTask;
            }

            download.Status = DownloadTaskStatus.Paused;
            download.CancelTokenSource?.Cancel();

            if (FileStreamHolder.TryGetValue(download, out var stream))
                stream.Dispose();

            Log.Info($"Paused downloading task :{download.DownloadInfo.FileName}");
            return Task.CompletedTask;
        }
    }
}
