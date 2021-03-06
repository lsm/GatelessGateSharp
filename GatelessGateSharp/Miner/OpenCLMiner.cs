﻿// Copyright 2017 Yurio Miyazawa (a.k.a zawawa) <me@yurio.net>
//
// This file is part of Gateless Gate Sharp.
//
// Gateless Gate Sharp is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Gateless Gate Sharp is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with Gateless Gate Sharp.  If not, see <http://www.gnu.org/licenses/>.



using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cloo;



namespace GatelessGateSharp
{
    class OpenCLMiner : Miner, IDisposable
    {
        public class ProgramArrayIndex
        {
            private int mDeviceIndex;
            private long mLocalWorkSize;

            public ProgramArrayIndex(int aDeviceIndex, long aLocalWorkSize)
            {
                mDeviceIndex = aDeviceIndex;
                mLocalWorkSize = aLocalWorkSize;
            }

            public bool Equals(ProgramArrayIndex mValue)
            {
                return mDeviceIndex == mValue.mDeviceIndex && mLocalWorkSize == mValue.mLocalWorkSize;
            }
        }

        private OpenCLDevice mDevice;
        private ComputeCommandQueue mQueue;

        public OpenCLDevice OpenCLDevice { get { return mDevice; } }
        public ComputeCommandQueue Queue { get { return mQueue; } }

        public ComputeDevice ComputeDevice { get { return mDevice.GetComputeDevice(); } }

        protected OpenCLMiner(OpenCLDevice aDevice, String aAlgorithmName, String aFirstAlgorithmName = "", String aSecondAlgorithmName = "")
            : base(aDevice, aAlgorithmName, aFirstAlgorithmName, aSecondAlgorithmName)
        {
            mDevice = aDevice;
            mQueue = new ComputeCommandQueue(Context, ComputeDevice, ComputeCommandQueueFlags.None);
        }

        protected ComputeProgram BuildProgram(string programName, long localWorkSize, string optionsAMD, string optionsNVIDIA, string optionsOthers) {
            ComputeProgram program;
            string defaultAssemblyFilePath = (OpenCLDevice.IsGCN3) ? @"AssemblyKernels\GCN3_" + programName + "_" + localWorkSize + ".isa"
                                                                   : null;
            string defultBinaryFilePath = @"BinaryKernels\" + ComputeDevice.Name + "_" + programName + "_" + localWorkSize + ".bin";
            string savedBinaryFilePath = (MainForm.SavedOpenCLBinaryKernelPathBase + @"\") + ComputeDevice.Name + "_" + programName + "_" + localWorkSize + ".bin";
            string sourceFilePath = @"Kernels\" + programName + ".cl";
            String buildOptions = (OpenCLDevice.GetVendor() == "AMD"    ? optionsAMD + " -D__AMD__" : 
                                   OpenCLDevice.GetVendor() == "NVIDIA" ? optionsNVIDIA + " -D__NVIDIA__" : 
                                                                          optionsOthers) + " -IKernels -DWORKSIZE=" + localWorkSize;
            Thread.CurrentThread.Priority = ThreadPriority.Highest;

            try {
                if (defaultAssemblyFilePath == null)
                    throw new Exception();
                CLRX assembler = new CLRX();
                program = new ComputeProgram(Context, new List<byte[]>() { assembler.Assemble(OpenCLDevice, defaultAssemblyFilePath) }, new List<ComputeDevice>() { ComputeDevice });
                MainForm.Logger("Loaded " + defaultAssemblyFilePath + " for Device #" + DeviceIndex + ".");
            } catch (Exception ex) {
                if (ex.Message != string.Empty) {
                    MainForm.Logger("Failed to load " + defaultAssemblyFilePath + ".");
                    MainForm.Logger(ex);
                }
                try {
                    if (!MainForm.UseDefaultOpenCLBinariesChecked)
                        throw new Exception();
                    byte[] binary = System.IO.File.ReadAllBytes(defultBinaryFilePath);
                    program = new ComputeProgram(Context, new List<byte[]>() { binary }, new List<ComputeDevice>() { ComputeDevice });
                    MainForm.Logger("Loaded " + defultBinaryFilePath + " for Device #" + DeviceIndex + ".");
                } catch (Exception) {
                    try {
                        if (!MainForm.ReuseCompiledBinariesChecked)
                            throw new Exception();
                        byte[] binary = System.IO.File.ReadAllBytes(savedBinaryFilePath);
                        program = new ComputeProgram(Context, new List<byte[]>() { binary }, new List<ComputeDevice>() { ComputeDevice });
                        MainForm.Logger("Loaded " + savedBinaryFilePath + " for Device #" + DeviceIndex + ".");
                    } catch (Exception) {
                        String source = System.IO.File.ReadAllText(sourceFilePath);
                        program = new ComputeProgram(Context, source);
                        MainForm.Logger(@"Loaded " + sourceFilePath + " for Device #" + DeviceIndex + ".");
                    }
                }
            }
            try {
                program.Build(OpenCLDevice.DeviceList, buildOptions, null, IntPtr.Zero);
                if (MainForm.ReuseCompiledBinariesChecked) {
                    try {
                        string tempFileName = System.IO.Path.GetTempPath() + Guid.NewGuid().ToString() + ".temp";
                        System.IO.File.WriteAllBytes(tempFileName, program.Binaries[0]);
                        System.IO.File.Copy(tempFileName, savedBinaryFilePath, true);
                        System.IO.File.Delete(tempFileName);
                    } catch (Exception ex) {
                        MainForm.Logger(ex);
                    }
                }
            } catch (Exception) {
                Thread.CurrentThread.Priority = Parameters.MinerThreadPriority;
                MainForm.Logger(program.GetBuildLog(ComputeDevice));
                program.Dispose();
                throw;
            }
            Thread.CurrentThread.Priority = Parameters.MinerThreadPriority;

            MainForm.Logger("Built " + programName + " program for Device #" + DeviceIndex + ".");
            MainForm.Logger("Build options: " + buildOptions);

            return program;
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                if (mQueue != null) {
                    mQueue.Dispose();
                    mQueue = null;
                }
            }
        }
    }
}

