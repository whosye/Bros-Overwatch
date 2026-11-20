using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

// The address book persists addresses and successful visits, never stale online statuses.
public static class ServerHistory
{
    const string Key="servers.history.v1";
    public const int Limit=128;
    [Serializable] public class Entry
    {
        public string address;
        public ushort port;
        public string label;
        public long joined;
        public string Endpoint=>address+":"+port;
        public string Name=>string.IsNullOrWhiteSpace(label)?Endpoint:label;
    }
    [Serializable] class Data { public List<Entry> entries=new List<Entry>(); }
    static Data data;
    public static IReadOnlyList<Entry> All { get { Load(); return data.entries; } }
    public static Entry LastJoined
    {
        get
        {
            Load(); Entry latest=null;
            foreach(var e in data.entries) if(e.joined>0&&(latest==null||e.joined>latest.joined)) latest=e;
            return latest;
        }
    }
    static void Load()
    {
        if(data!=null)return;
        try { data=JsonUtility.FromJson<Data>(PlayerPrefs.GetString(Key,"")); } catch(Exception) { data=null; }
        if(data==null)data=new Data(); if(data.entries==null)data.entries=new List<Entry>();
        var unique=new HashSet<string>();
        data.entries.RemoveAll(e=>e==null||!TryParse(e.Endpoint,out _,out _,out _)||!unique.Add(e.Endpoint));
        if(data.entries.Count>Limit)data.entries.RemoveRange(Limit,data.entries.Count-Limit);
    }
    public static bool TryParse(string input,out string address,out ushort port,out string error)
    {
        address=""; port=GameConnection.Port; error="";
        var p=(input??"").Trim().Split(':');
        if(p.Length>2||(p.Length==2&&(!ushort.TryParse(p[1],out port)||port==0)))
        { error="Použij IP:port, například 192.168.1.12:7777."; return false; }
        string host=p[0].Trim(); if(string.Equals(host,"localhost",StringComparison.OrdinalIgnoreCase))host="127.0.0.1";
        if(!IPAddress.TryParse(host,out var ip)||ip.AddressFamily!=AddressFamily.InterNetwork||ip.Equals(IPAddress.Any)||ip.Equals(IPAddress.Broadcast)||ip.GetAddressBytes()[0]>=224)
        { error="Zadej IPv4 adresu hostitele, například 192.168.1.12."; return false; }
        address=ip.ToString(); return true;
    }
    public static Entry Add(string input,string label,out string error)
    {
        Load(); if(!TryParse(input,out string address,out ushort port,out error))return null;
        var entry=data.entries.Find(e=>e.address==address&&e.port==port);
        if(entry==null)
        {
            if(data.entries.Count>=Limit) { error="Seznam je plný. Nejdřív některý server odeber."; return null; }
            entry=new Entry { address=address,port=port }; data.entries.Add(entry);
        }
        if(!string.IsNullOrWhiteSpace(label))entry.label=label.Trim().Substring(0,Mathf.Min(40,label.Trim().Length));
        Save(); return entry;
    }
    public static void Joined(string address,ushort port,string name)
    {
        var entry=Add(address+":"+port,"",out _); if(entry==null)return;
        if(string.IsNullOrWhiteSpace(entry.label))entry.label=name;
        entry.joined=DateTime.UtcNow.Ticks; Save();
    }
    public static void Remove(string endpoint) { Load(); data.entries.RemoveAll(e=>e.Endpoint==endpoint); Save(); }
    static void Save() { PlayerPrefs.SetString(Key,JsonUtility.ToJson(data)); PlayerPrefs.Save(); }
    public static void Reload() { data=null; Load(); }
}
