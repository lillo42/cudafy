﻿/*
CUDAfy.NET - LGPL 2.1 License
Please consider purchasing a commerical license - it helps development, frees you from LGPL restrictions
and provides you with support.  Thank you!
Copyright (C) 2011 Hybrid DSP Systems
http://www.hybriddsp.com

This library is free software; you can redistribute it and/or
modify it under the terms of the GNU Lesser General Public
License as published by the Free Software Foundation; either
version 2.1 of the License, or (at your option) any later version.

This library is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
Lesser General Public License for more details.

You should have received a copy of the GNU Lesser General Public
License along with this library; if not, write to the Free Software
Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
*/
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Cudafy.Host
{
    /// <summary>
    /// CudafyHost contains high level management operations.
    /// </summary>
    public sealed class CudafyHost
    {
        static CudafyHost()
        {
            GetDevice(eGPUType.Emulator, 0);
        }
        
        private static Dictionary<string, GPGPU> GPGPUs = new Dictionary<string, GPGPU>();

        /// <summary>
        /// Gets the device properties.
        /// </summary>
        /// <param name="type">The type of GPU.</param>
        /// <param name="useAdvanced">Whether to get the additional device settings via the cudart dll.</param>
        /// <returns>Device properties for all devices of the specified type.</returns>
        public static IEnumerable<GPGPUProperties> GetDeviceProperties(eGPUType type, bool useAdvanced = true)
        {
            if (type == eGPUType.Emulator)
            {
                foreach (var kvp in GPGPUs.Where(g => g.Value is EmulatedGPU))
                    yield return kvp.Value.GetDeviceProperties(useAdvanced);
            }
            else if (type == eGPUType.Cuda)
            {
                int devCnt = CudaGPU.GetDeviceCount();
                for (int i = 0; i < devCnt; i++)
                {
                    GPGPU gpu = null;
                    GPGPUProperties props;
                    if (DeviceCreated(type, i))
                    {
                        gpu = GetDevice(eGPUType.Cuda, i);
                        props = gpu.GetDeviceProperties(useAdvanced);
                    }
                    else
                    {
                        gpu = GetDevice(eGPUType.Cuda, i);
                        props = gpu.GetDeviceProperties(useAdvanced);
                        RemoveDevice(gpu);
                    }
                    yield return props;
                }
            }
            else
                throw new CudafyHostException(CudafyHostException.csX_NOT_CURRENTLY_SUPPORTED, type);
        }

        /// <summary>
        /// Gets the device count.
        /// </summary>
        /// <param name="type">The type of device.</param>
        /// <returns>Number of devices of type specified.</returns>
        public static int GetDeviceCount(eGPUType type)
        {
            int cnt = 0;
            if (type == eGPUType.Emulator)
            {
                cnt = GPGPUs.Count(g => g.Key.StartsWith(eGPUType.Emulator.ToString()));
                if (cnt == 0)
                {
                    GetDevice(eGPUType.Emulator, 0);
                    cnt++;
                }
            }
            else if (type == eGPUType.Cuda)
                cnt += CudaGPU.GetDeviceCount();
            else
                throw new CudafyHostException(CudafyHostException.csX_NOT_CURRENTLY_SUPPORTED, type);
            return cnt;
        }

        /// <summary>
        /// Gets device of type specified from the cache. Creates one if it does not already exist.
        /// </summary>
        /// <param name="type">The target type.</param>
        /// <param name="deviceId">The device id.</param>
        /// <returns>GPGPU instance.</returns>
        public static GPGPU GetDevice(eGPUType type = eGPUType.Cuda, int deviceId = 0)
        {
            string name = BuildGPUName(type, deviceId);
            GPGPU gpu = null;
            if (!GPGPUs.ContainsKey(name))
            {
                return CreateDevice(type, deviceId);
            }
            else
            {
                gpu = GPGPUs[name];
                if(gpu.IsDisposed)
                {
                    gpu = CreateDevice(type, deviceId); 
                }
            }
               
            return gpu;
        }

        /// <summary>
        /// Checks if the specified device has already been created and added to the cache.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="deviceId">The device id.</param>
        /// <returns>True if created, else false.</returns>
        public static bool DeviceCreated(eGPUType type, int deviceId = 0)
        {
            string name = BuildGPUName(type, deviceId);
            return GPGPUs.ContainsKey(name);
        }

        /// <summary>
        /// Obsolete. Use GetDevice instead.
        /// </summary>
        /// <param name="type">The target type.</param>
        /// <param name="deviceId">The device id.</param>
        /// <returns>GPGPU instance.</returns>
        [Obsolete("Use GetDevice instead.")]
        public static GPGPU GetGPGPU(eGPUType type, int deviceId = 0)
        {
            return GetDevice(type, deviceId);
        }

        private static string BuildGPUName(eGPUType type, int deviceId)
        {
            string name = type.ToString() + deviceId.ToString();
            return name;
        }

        /// <summary>
        /// Creates a new GPGPU and adds to cache. If GPGPU already exists then it is first destroyed and removed from cache.
        /// </summary>
        /// <param name="type">The target type.</param>
        /// <param name="deviceId">The device id.</param>
        /// <returns>GPGPU instance.</returns>
        public static GPGPU CreateDevice(eGPUType type, int deviceId = 0)
        {
            string name = BuildGPUName(type, deviceId);
            GPGPU gpu;
            if (GPGPUs.ContainsKey(name))
            {
                gpu = GPGPUs[name];
                RemoveDevice(gpu);
            }
            gpu = DoCreateDevice(type, deviceId);
            GPGPUs.Add(name, gpu);
            return gpu; 
        }

        /// <summary>
        /// Removes the specified GPGPU from the cache.
        /// </summary>
        /// <param name="gpu">The gpu.</param>
        /// <returns>True if gpu was removed, else false.</returns>
        public static bool RemoveDevice(GPGPU gpu)
        {
            List<GPGPU> gpus = GPGPUs.Values.Where(v => v == gpu).ToList();
            bool removed = gpus.Count > 0;
            List<string> names = new List<string>();
            for (int i = 0; i < gpus.Count; i++)
            {
                gpus[i].Dispose();
                
                foreach (var v in GPGPUs)
                    if (v.Value == gpu)
                        names.Add(v.Key);
            }
            foreach (var s in names.Distinct())
                GPGPUs.Remove(s);

            return removed;
        }

        /// <summary>
        /// Clears all gpus from the cache.
        /// </summary>
        /// <returns>The number of gpus removed.</returns>
        public static int ClearDevices()
        {
            List<GPGPU> gpus = GPGPUs.Values.ToList();
            int cnt = 0;
            foreach (var g in gpus)
                cnt += (RemoveDevice(g) ? 1 : 0);
            return cnt;
        }

        /// <summary>
        /// Clears all created device memories.
        /// </summary>
        public static void ClearAllDeviceMemories()
        {
            foreach (var kvp in GPGPUs)
                kvp.Value.FreeAll();
        }

        private static GPGPU DoCreateDevice(eGPUType target, int deviceId = 0)
        {
            try
            {
                if (target == eGPUType.Cuda)
                    return new CudaGPU(deviceId);
                else if (target == eGPUType.Emulator)
                    return new EmulatedGPU(deviceId);
                else 
                    throw new CudafyHostException(CudafyHostException.csX_NOT_CURRENTLY_SUPPORTED, target);
            }
            catch (Exception)
            {
                throw;
            }
            throw new NotSupportedException(target.ToString());
        }
    }
}

      