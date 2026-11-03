using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ZapretDesktop
{
    public sealed class InterfaceDohProperty
    {
        public uint Index { get; set; }
        public ulong Flags { get; set; }
        public string Template { get; set; }
    }
    public sealed class InterfaceDnsState
    {
        public string NameServer { get; set; }
        public string ProfileNameServer { get; set; }
        public InterfaceDohProperty[] ServerProperties { get; set; }
        public InterfaceDohProperty[] ProfileServerProperties { get; set; }
    }
    // Documented IP Helper API, DNS_INTERFACE_SETTINGS_VERSION3.
    // Select the same IP family for reads and writes. Windows 11 returns the
    // IPv4 configuration for Flags=0, and IPv6 for DNS_SETTING_IPV6.
    public static class InterfaceDns
    {
        [StructLayout(LayoutKind.Sequential)] struct Settings
        {
            public uint Version; public ulong Flags;
            public IntPtr Domain,NameServer,SearchList;
            public uint RegistrationEnabled,RegisterAdapterName,EnableLLMNR,QueryAdapterName;
            public IntPtr ProfileNameServer; public uint DisableUnconstrainedQueries;
            public IntPtr SupplementalSearchList; public uint Count; public IntPtr Properties;
            public uint ProfileCount; public IntPtr ProfileProperties;
        }
        [StructLayout(LayoutKind.Sequential)] struct Property
        {
            public uint Version,Index,Type; public IntPtr Value;
        }
        [StructLayout(LayoutKind.Sequential)] struct Doh
        {
            public IntPtr Template; public ulong Flags;
        }
        [DllImport("iphlpapi.dll",ExactSpelling=true)] static extern uint GetInterfaceDnsSettings(Guid id,ref Settings settings);
        [DllImport("iphlpapi.dll",ExactSpelling=true)] static extern uint SetInterfaceDnsSettings(Guid id,ref Settings settings);
        [DllImport("iphlpapi.dll",ExactSpelling=true)] static extern void FreeInterfaceDnsSettings(ref Settings settings);
        static string StringAt(IntPtr value) { return value==IntPtr.Zero ? null : Marshal.PtrToStringUni(value); }
        static string[] Addresses(string value)
        {
            if(String.IsNullOrWhiteSpace(value)) return new string[0];
            var result=Regex.Split(value.Trim(),@"[,\s]+");
            foreach(var ip in result) { IPAddress parsed; if(!IPAddress.TryParse(ip,out parsed)) throw new IOException("Windows returned invalid adapter DNS addresses."); }
            return result;
        }
        static InterfaceDohProperty[] ReadProperties(IntPtr pointer,uint count)
        {
            if(count>256 || count!=0 && pointer==IntPtr.Zero) throw new IOException("Invalid adapter DNS properties.");
            var result=new List<InterfaceDohProperty>();
            for(int i=0;i<count;i++) {
                var property=(Property)Marshal.PtrToStructure(IntPtr.Add(pointer,i*Marshal.SizeOf(typeof(Property))),typeof(Property));
                if(property.Version!=1 || property.Type!=1 || property.Value==IntPtr.Zero) throw new IOException("Unsupported adapter DNS property.");
                var doh=(Doh)Marshal.PtrToStructure(property.Value,typeof(Doh));
                result.Add(new InterfaceDohProperty {Index=property.Index,Flags=doh.Flags,Template=StringAt(doh.Template)});
            }
            return result.ToArray();
        }
        static string Filter(string value,InterfaceDohProperty[] properties,bool v6,out InterfaceDohProperty[] selected)
        {
            var addresses=Addresses(value); var filtered=new List<string>(); var props=new List<InterfaceDohProperty>();
            foreach(var property in properties) if(property.Index>=addresses.Length) throw new IOException("Invalid DNS server property index.");
            for(int i=0;i<addresses.Length;i++) {
                if((IPAddress.Parse(addresses[i]).AddressFamily==AddressFamily.InterNetworkV6)!=v6) continue;
                foreach(var property in properties.Where(p=>p.Index==i)) props.Add(new InterfaceDohProperty {Index=(uint)filtered.Count,Flags=property.Flags,Template=property.Template});
                filtered.Add(addresses[i]);
            }
            selected=props.ToArray(); return filtered.Count==0 ? null : String.Join(",",filtered);
        }
        public static InterfaceDnsState Read(Guid id,bool ipv6)
        {
            var settings=new Settings {Version=3,Flags=ipv6 ? 1UL : 0UL};
            uint error=GetInterfaceDnsSettings(id,ref settings);
            if(error!=0) throw new Win32Exception((int)error,"Cannot read adapter DoH settings: "+new Win32Exception((int)error).Message);
            try {
                InterfaceDohProperty[] properties,profile;
                string name=Filter(StringAt(settings.NameServer),ReadProperties(settings.Properties,settings.Count),ipv6,out properties);
                string profileName=Filter(StringAt(settings.ProfileNameServer),ReadProperties(settings.ProfileProperties,settings.ProfileCount),ipv6,out profile);
                return new InterfaceDnsState {NameServer=name,ProfileNameServer=profileName,ServerProperties=properties,ProfileServerProperties=profile};
            } finally { FreeInterfaceDnsSettings(ref settings); }
        }
        static IntPtr Allocate(int size,List<IntPtr> allocations) { var value=Marshal.AllocHGlobal(size); allocations.Add(value); return value; }
        static IntPtr StringPointer(string text,List<IntPtr> allocations) { if(text==null) return IntPtr.Zero; var value=Marshal.StringToHGlobalUni(text); allocations.Add(value); return value; }
        static IntPtr BuildProperties(string servers,InterfaceDohProperty[] properties,bool v6,List<IntPtr> allocations)
        {
            var addresses=Addresses(servers); if(properties==null) properties=new InterfaceDohProperty[0];
            if(properties.Length>256 || properties.Select(p=>p.Index).Distinct().Count()!=properties.Length) throw new IOException("Invalid adapter DoH properties.");
            foreach(var ip in addresses) if((IPAddress.Parse(ip).AddressFamily==AddressFamily.InterNetworkV6)!=v6) throw new IOException("Wrong DNS address family.");
            if(properties.Length==0) return IntPtr.Zero;
            var array=Allocate(Marshal.SizeOf(typeof(Property))*properties.Length,allocations);
            for(int i=0;i<properties.Length;i++) {
                var property=properties[i];
                if(property.Index>=addresses.Length || (property.Flags&3)==3) throw new IOException("Invalid adapter DoH server settings.");
                var doh=Allocate(Marshal.SizeOf(typeof(Doh)),allocations);
                // Get may expand an automatic server's template. Set requires
                // NULL for automatic mode; keep its flags (including DDR).
                Marshal.StructureToPtr(new Doh {Flags=property.Flags,Template=StringPointer((property.Flags&1)!=0 ? null : property.Template,allocations)},doh,false);
                Marshal.StructureToPtr(new Property {Version=1,Type=1,Index=property.Index,Value=doh},IntPtr.Add(array,i*Marshal.SizeOf(typeof(Property))),false);
            }
            return array;
        }
        static Settings Build(InterfaceDnsState state,bool v6,List<IntPtr> allocations)
        {
            if(state==null) throw new IOException("Missing adapter DNS state.");
            var properties=state.ServerProperties ?? new InterfaceDohProperty[0]; var profile=state.ProfileServerProperties ?? new InterfaceDohProperty[0];
            var result=new Settings {Version=3,Flags=(v6 ? 1UL : 0UL)|2UL};
            result.Properties=BuildProperties(state.NameServer,properties,v6,allocations); result.Count=(uint)properties.Length;
            result.ProfileProperties=BuildProperties(state.ProfileNameServer,profile,v6,allocations); result.ProfileCount=(uint)profile.Length;
            if(!String.IsNullOrEmpty(state.NameServer)) result.Flags|=0x1000;
            if(!String.IsNullOrEmpty(state.ProfileNameServer)) result.Flags|=0x2200;
            result.NameServer=StringPointer(state.NameServer,allocations); result.ProfileNameServer=StringPointer(state.ProfileNameServer,allocations);
            return result;
        }
        public static void Write(Guid id,bool ipv6,string json)
        {
            var state=new JavaScriptSerializer().Deserialize<InterfaceDnsState>(json); var allocations=new List<IntPtr>();
            try { var settings=Build(state,ipv6,allocations); uint error=SetInterfaceDnsSettings(id,ref settings); if(error!=0) throw new Win32Exception((int)error,"Cannot set adapter DoH settings: "+new Win32Exception((int)error).Message); }
            finally { foreach(var pointer in allocations) Marshal.FreeHGlobal(pointer); }
        }
        public static void Validate(bool ipv6,string json)
        {
            var state=new JavaScriptSerializer().Deserialize<InterfaceDnsState>(json); var allocations=new List<IntPtr>();
            try { Build(state,ipv6,allocations); }
            finally { foreach(var pointer in allocations) Marshal.FreeHGlobal(pointer); }
        }
        internal static void Test()
        {
            if(IntPtr.Size!=8 || Marshal.SizeOf(typeof(Settings))!=112 || Marshal.SizeOf(typeof(Property))!=24 || Marshal.SizeOf(typeof(Doh))!=16 || Marshal.OffsetOf(typeof(Settings),"Properties").ToInt32()!=88) throw new Exception("FAILED DNS native x64 layout");
            var state=new InterfaceDnsState {NameServer="1.1.1.1,1.0.0.1",ProfileNameServer="1.1.1.1,1.0.0.1",ServerProperties=new[]{new InterfaceDohProperty {Index=0,Flags=1},new InterfaceDohProperty {Index=1,Flags=1}}}; state.ProfileServerProperties=state.ServerProperties;
            var allocations=new List<IntPtr>();
            try { var native=Build(state,false,allocations); var properties=ReadProperties(native.Properties,native.Count); if(native.Flags!=0x3202 || properties.Length!=2 || properties[1].Index!=1 || properties.Any(p=>p.Flags!=1 || p.Template!=null)) throw new Exception("FAILED DNS automatic DoH marshaling"); }
            finally { foreach(var pointer in allocations) Marshal.FreeHGlobal(pointer); }
            state.ServerProperties=new[]{new InterfaceDohProperty {Index=0,Flags=17,Template="https://dns.example/query"}};
            state.ProfileNameServer=null; state.ProfileServerProperties=new InterfaceDohProperty[0]; allocations=new List<IntPtr>();
            try { var native=Build(state,false,allocations); var properties=ReadProperties(native.Properties,native.Count); if(properties[0].Flags!=17 || properties[0].Template!=null) throw new Exception("FAILED expanded automatic DNS template normalization"); }
            finally { foreach(var pointer in allocations) Marshal.FreeHGlobal(pointer); }
            InterfaceDohProperty[] filtered; string v6=Filter("1.1.1.1,2606:4700:4700::1111",new[]{new InterfaceDohProperty {Index=1,Flags=1}},true,out filtered);
            if(v6!="2606:4700:4700::1111" || filtered.Length!=1 || filtered[0].Index!=0) throw new Exception("FAILED DNS family property index remapping");
        }
    }
}
