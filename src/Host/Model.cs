using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PclPatchHost
{
    public enum TargetProfile
    {
        Unknown,
        Pcl2,
        PclCe,
        Hmcl
    }

    public sealed class TargetInfo
    {
        public TargetProfile Profile;
        public bool Supported;
        public bool Patched;
        public bool NoGate;
        public string Version = "";
        public string State = "unsupported";
        public string Error = "";
        public int MarkerCount;
        public string[] Markers = new string[0];
        public string[] Methods = new string[0];
        public string AssemblyName = "";
        public PclPatchInfo Pcl2;
        public PclCePatchInfo PclCe;
        public HMCLPatchInfo Hmcl;

        public bool IsKnown
        {
            get { return Profile != TargetProfile.Unknown; }
        }

        public string ProfileLabel
        {
            get
            {
                if (Profile == TargetProfile.Pcl2) return "PCL2";
                if (Profile == TargetProfile.PclCe) return "PCL2 CE";
                if (Profile == TargetProfile.Hmcl) return "HMCL";
                return "未知";
            }
        }

        public string StateLabel
        {
            get
            {
                if (NoGate) return "无需处理";
                if (Patched) return "已修补";
                if (State == "partial" || State == "modified") return "部分修改";
                if (!Supported) return "结构不支持";
                return "原版";
            }
        }
    }

    public static class TargetProbe
    {
        public static TargetInfo Probe(string path)
        {
            var result = new TargetInfo();
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                result.Error = "目标文件不存在。";
                return result;
            }

            try
            {
                var ce = PclCePatchBuilder.Verify(path);
                if (ce != null && ce.IsPclCe)
                {
                    result.Profile = TargetProfile.PclCe;
                    result.PclCe = ce;
                    result.Supported = ce.Supported;
                    result.Patched = ce.Patched;
                    result.Version = ce.Version;
                    result.State = ce.State;
                    result.Error = ce.Error;
                    result.Markers = ce.Markers ?? new string[0];
                    result.MarkerCount = result.Markers.Length;
                    result.Methods = ce.Targets ?? new string[0];
                    result.AssemblyName = ce.CoreAssembly;
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            try
            {
                var hm = HMCLPatchCore.Inspect(path);
                if (hm != null && IsHmcl(hm))
                {
                    result.Profile = TargetProfile.Hmcl;
                    result.Hmcl = hm;
                    result.NoGate = String.Equals(hm.State, "no_gate", StringComparison.OrdinalIgnoreCase);
                    result.Patched = String.Equals(hm.State, "patched", StringComparison.OrdinalIgnoreCase);
                    result.Supported = !String.Equals(hm.State, "unsupported", StringComparison.OrdinalIgnoreCase);
                    result.Version = hm.Version;
                    result.State = hm.State;
                    result.Error = hm.Message;
                    result.Markers = result.Patched ? new string[] { "HMCL-PATCH-OPT" } : new string[0];
                    result.MarkerCount = result.Markers.Length;
                    result.Methods = new string[] { "VersionsPage -> RegionUtils.isIsChinaMainlandNetwork" };
                    result.AssemblyName = hm.TargetClass;
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            try
            {
                var pcl = PclDynamicPatchBuilder.Verify(path);
                if (pcl != null && (pcl.Supported || String.Equals(pcl.AssemblyName, "Plain Craft Launcher 2", StringComparison.OrdinalIgnoreCase)))
                {
                    result.Profile = TargetProfile.Pcl2;
                    result.Pcl2 = pcl;
                    result.Supported = pcl.Supported;
                    result.Patched = pcl.Patched;
                    result.Version = pcl.VersionBaseName;
                    result.State = pcl.State;
                    result.Error = pcl.Error;
                    result.Markers = pcl.Markers ?? new string[0];
                    result.MarkerCount = pcl.MarkerCount;
                    result.Methods = pcl.Methods ?? new string[0];
                    result.AssemblyName = pcl.AssemblyName;
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            result.Profile = TargetProfile.Unknown;
            result.State = "unsupported";
            result.Supported = false;
            result.Error = String.IsNullOrWhiteSpace(result.Error) ? "无法自动识别目标类型。" : result.Error;
            return result;
        }

        private static bool IsHmcl(HMCLPatchInfo info)
        {
            if (info == null) return false;
            if (String.Equals(info.State, "patchable", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(info.State, "patched", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(info.State, "no_gate", StringComparison.OrdinalIgnoreCase))
                return true;
            if (!String.IsNullOrWhiteSpace(info.Version)) return true;
            if (info.Signed) return true;
            return false;
        }
    }
}
