﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Wbooru.PluginExt;

namespace Wbooru.Utils
{
    public abstract class ObjectPoolBase
    {
        public abstract int CachingObjectCount { get; }
        public static int MaxTempCache { get; set; } = 10;

        internal void OnPreReduceSchedule()
        {
            var before = CachingObjectCount;
            OnReduceObjects();
            var after = CachingObjectCount;

            var diff = after - before;

            if (diff < 0)
                Log.Debug($"Reduced {diff} {GetType().GenericTypeArguments?.FirstOrDefault()?.Name ?? "unknown type"} objects");

        }

        protected abstract void OnReduceObjects();
    }

    [Export(typeof(ISchedulable))]
    [Export(typeof(ObjectPoolManager))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class ObjectPoolManager : ISchedulable
    {
        public string SchedulerName => "Object Pool Maintenance Scheduler";

        public TimeSpan ScheduleCallLoopInterval { get; } = TimeSpan.FromSeconds(10);

        HashSet<ObjectPoolBase> object_pools = new HashSet<ObjectPoolBase>();

        public void RegisterNewObjectPool(ObjectPoolBase pool)
        {
            if (pool == null)
                return;

            object_pools.Add(pool);
            Log.Debug($"Register new object pool :{pool.GetType().Name}");
        }

        public void OnSchedulerTerm()
        {

        }

        public Task OnScheduleCall(CancellationToken cancellationToken)
        {
            foreach (var pool in object_pools)
                pool.OnPreReduceSchedule();

            return Task.CompletedTask;
        }
    }

    public class ObjectPool<T> : ObjectPoolBase where T : new()
    {
        #region AutoImpl

        private static ObjectPool<T> instance;
        private static ObjectPool<T> Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new ObjectPool<T>();
                    Container.Default.GetExportedValue<ObjectPoolManager>().RegisterNewObjectPool(instance);
                }

                return instance;
            }
        }

        #endregion

        private HashSet<T> cache_obj = new HashSet<T>();

        public static bool EnableTrim { get; set; } = true;

        public override int CachingObjectCount => cache_obj.Count;

        protected override void OnReduceObjects()
        {
            if (!EnableTrim)
                return;

            var count = CachingObjectCount > MaxTempCache ?
                (MaxTempCache + ((CachingObjectCount - MaxTempCache) / 2)) :
                CachingObjectCount / 4; ;

            for (int i = 0; i < count / 2; i++)
                cache_obj.Remove(cache_obj.First());
        }

        #region Sugar~

        public class AutoDisposable : IDisposable
        {
            public AutoDisposable(T obj) => this.obj = obj;

            private T obj;

            public void Dispose()
            {
                Return(obj);
            }
        }

        public static IDisposable GetWithUsingDisposable(out T obj, out bool isNewObject)
        {
            isNewObject = Get(out obj);
            return new AutoDisposable(obj);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="obj">gained object</param>
        /// <returns>it's a new object if returns true</returns>
        public static bool Get(out T obj)
        {
            var cache_obj = Instance.cache_obj;

            if (cache_obj.Count == 0)
            {
                obj = new T();
                return true;
            }

            obj = cache_obj.First();
            cache_obj.Remove(obj);

            (obj as ICacheCleanable)?.OnBeforeGetClean();
            return false;
        }

        public static T Get()
        {
            Get(out var t);
            return t;
        }

        public static void Return(T obj)
        {
            if (obj == null)
                return;

            Instance.cache_obj.Add(obj);
            (obj as ICacheCleanable)?.OnAfterPutClean();
        }

        #endregion
    }
}
