// RoundTripCheck: comprobacion REAL de reversibilidad sobre este Windows. Se ejecuta elevado.
//
//   RoundTripCheck.exe [informe.txt] [id1,id2,...]
//
// Para cada ajuste seguro o moderado (mas hpet_off, bcd_timer y gpu_msi_mode): foto del estado -> aplicar -> foto ->
// revertir -> foto, y comprueba que la primera y la ultima son identicas. Despues aplica todos y los revierte en orden
// LIFO, como hace "Revertir TODO", y compara con el estado inicial. Las fotos incluyen valores de registro, planes de
// energia, servicios (estado y arranque), tareas programadas, hibernacion, bcdedit, HPET y MSI.
//
// ADVERTENCIA: modifica el sistema de forma temporal y crea un punto de restauracion al empezar (si la proteccion del
// sistema esta activa). NO ejecuta ajustes que debilitan la seguridad (Defender, firewall, UAC, VBS/HVCI, hipervisor),
// ni el debloat de Appx (no reversible), ni la instalacion de la tarea del timer residente. Los backups van a una
// carpeta temporal, no a %ProgramData%\SotheOptimizer.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using Sothe;

// Banco de pruebas REAL de reversibilidad. Se ejecuta elevado. Para cada ajuste seguro/moderado:
// foto del estado -> aplicar -> foto -> revertir -> foto, y compara la primera con la ultima.
// Despues aplica todos y los revierte en orden LIFO (lo que hace "Revertir TODO"). Los backups van a una carpeta temporal.
internal static class Program
{
    private static StreamWriter _rep;
    private static void Out(string s) { Console.WriteLine(s); _rep.WriteLine(s); _rep.Flush(); }

    private static readonly HashSet<string> Skip = new HashSet<string> { "timer_res_fixed", "reserved_storage_off" };
    private static readonly HashSet<string> Extra = new HashSet<string> { "hpet_off", "bcd_timer", "gpu_msi_mode" };

    private static int Main(string[] args)
    {
        var reportPath = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "toji-roundtrip.txt");
        _rep = new StreamWriter(reportPath, false, new UTF8Encoding(false));
        var root = Path.Combine(Path.GetTempPath(), "toji-rt-" + Guid.NewGuid().ToString("N"));
        var eng = new SotheEngine(AppContext.BaseDirectory, root);
        var all = eng.LoadTweaks();
        var sel = all.Where(t => ((t.Danger == "safe" || t.Danger == "moderate") || Extra.Contains(t.Id)) && !Skip.Contains(t.Id)).ToList();
        if (args.Length > 1) { var only = new HashSet<string>(args[1].Split(',')); sel = sel.Where(t => only.Contains(t.Id)).ToList(); }
        Out($"Toji round-trip check  {DateTime.Now:yyyy-MM-dd HH:mm:ss}  tweaks: {sel.Count} of {all.Count} (safe+moderate+boot-timer/HPET/MSI; skipped: {string.Join(",", Skip)})");
        Out($"OS: {Environment.OSVersion}  user: {Environment.UserName}");

        bool rp = false;
        try { rp = eng.CreateRestorePoint(); } catch (Exception ex) { Out("restore point exception: " + ex.Message); }
        Out("Restore point created: " + rp);
        Out("");

        int exit = 0;
        try
        {
            var baseline = Snap(sel);
            Out("=== PHASE 1: each tweak apply -> revert ===");
            Out("id | apply | effective | revert | restored");
            int restoredCount = 0, notEffective = 0, failures = 0;
            foreach (var t in sel)
            {
                var s0 = Snap(sel);
                var a = eng.ApplyTweak(t);
                var s1 = Snap(sel);
                var r = eng.RevertTweak(t.Id);
                var s2 = Snap(sel);
                var changed = Diff(s0, s1);
                var left = Diff(s0, s2);
                bool effective = changed.Count > 0;
                bool restored = left.Count == 0;
                if (restored) restoredCount++;
                if (!effective) notEffective++;
                if (!a.Ok || !r.Ok || !restored) failures++;
                Out($"{t.Id} | {(a.Ok ? "ok" : "ERR")} | {(effective ? "yes" : "NO-CHANGE-DETECTED")} | {(r.Ok ? "ok" : "ERR")} | {(restored ? "RESTORED" : "NOT-RESTORED")}");
                foreach (var e in a.Errors) Out("    apply error: " + e);
                foreach (var e in r.Errors) Out("    revert error: " + e);
                if (!restored) foreach (var d in left) Out("    left behind: " + d);
            }
            Out($"PHASE 1 summary: restored {restoredCount}/{sel.Count}, tweaks with no detectable effect {notEffective}, with problems {failures}");
            Out("");

            Out("=== PHASE 2: apply ALL, then revert newest-first (Revert All) ===");
            foreach (var t in sel) { var a = eng.ApplyTweak(t); if (!a.Ok) Out($"  apply {t.Id}: {string.Join("; ", a.Errors)}"); }
            var applied = eng.AppliedNewestFirst();
            Out($"  applied: {applied.Count}");
            var errs = 0;
            foreach (var id in applied) { var r = eng.RevertTweak(id); if (!r.Ok) { errs++; Out($"  revert {id}: {string.Join("; ", r.Errors)}"); } }
            var final = Snap(sel);
            var diff = Diff(baseline, final);
            Out($"PHASE 2 result: revert errors {errs}, differences vs baseline: {diff.Count}");
            foreach (var d in diff) Out("    left behind: " + d);
            Out("backups left: " + eng.GetApplied().Count);
            exit = (diff.Count == 0 && errs == 0) ? 0 : 2;
        }
        catch (Exception ex)
        {
            Out("FATAL: " + ex);
            exit = 3;
        }
        finally
        {
            foreach (var id in eng.AppliedNewestFirst())
            {
                var r = eng.RevertTweak(id);
                Out($"cleanup revert {id}: {(r.Ok ? "ok" : string.Join("; ", r.Errors))}");
            }
            _rep.Flush(); _rep.Dispose();
            try { Directory.Delete(root, true); } catch { }
        }
        return exit;
    }

    private static List<string> Diff(SortedDictionary<string, string> a, SortedDictionary<string, string> b)
    {
        var d = new List<string>();
        foreach (var k in a.Keys.Union(b.Keys).OrderBy(x => x, StringComparer.Ordinal))
        {
            a.TryGetValue(k, out var va); b.TryGetValue(k, out var vb);
            if (va != vb) d.Add($"{k}: '{va ?? "<absent>"}' -> '{vb ?? "<absent>"}'");
        }
        return d;
    }

    private static SortedDictionary<string, string> Snap(List<Tweak> tweaks)
    {
        var d = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var services = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tweaks)
            foreach (var op in t.Ops)
            {
                if (op.Type == "reg") d[$"REG {op.Path} {op.Name}"] = ReadReg(op.Path, op.Name);
                else if (op.Type == "service") { services.Add(op.Name); d[$"REG svc:{op.Name} Start"] = ReadReg(@"HKLM:\SYSTEM\CurrentControlSet\Services\" + op.Name, "Start"); }
            }
        // Nagle: valores por interfaz de red
        using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces"))
            if (k != null)
                foreach (var name in k.GetSubKeyNames())
                    foreach (var v in new[] { "TcpAckFrequency", "TCPNoDelay" })
                        d[$"REG iface {name} {v}"] = ReadReg(@"HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + name, v);
        d["REG HibernateEnabled"] = ReadReg(@"HKLM:\SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled");

        var svcList = string.Join(",", services.Select(s => "'" + s + "'"));
        var ps = @"
$o = New-Object System.Collections.Generic.List[string]
$o.Add('power.active=' + ((powercfg /getactivescheme) -join ' '))
$o.Add('power.plans=' + ((powercfg /list | Select-String -Pattern 'GUID' | % { ($_.Line -replace '\s+',' ').Trim() } | Sort-Object) -join ' ; '))
foreach ($a in 'PROCTHROTTLEMIN','PROCTHROTTLEMAX','CPMINCORES','IDLEDISABLE') {
  $q = powercfg /query SCHEME_CURRENT SUB_PROCESSOR $a 2>$null
  $o.Add('power.' + $a + '=' + (($q | Select-String -Pattern '0x[0-9a-fA-F]{8}' | % { $_.Line.Trim() }) -join ' | '))
}
$o.Add('hiberfil=' + (Test-Path -LiteralPath ($env:SystemDrive + '\hiberfil.sys') -ErrorAction SilentlyContinue))
foreach ($s in @(" + svcList + @")) {
  $sv = Get-Service -Name $s -ErrorAction SilentlyContinue
  if ($sv) { $o.Add('svc.' + $s + '=' + $sv.Status + '/' + $sv.StartType) } else { $o.Add('svc.' + $s + '=missing') }
}
Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { $_.TaskName -match 'Compatibility Appraiser|Consolidator|UsbCeip|Nv|NVIDIA' } | ForEach-Object { $o.Add('task.' + $_.TaskPath + $_.TaskName + '=' + $_.State) }

$b = (bcdedit /enum '{current}' | Out-String)
foreach ($n in 'useplatformclock','useplatformtick','disabledynamictick','hypervisorlaunchtype') { $m = [regex]::Match($b, '(?m)^\s*' + $n + '\s+(\S+)'); $o.Add('bcd.' + $n + '=' + $m.Groups[1].Value) }
Get-NetFirewallProfile -ErrorAction SilentlyContinue | ForEach-Object { $o.Add('fw.' + $_.Name + '=' + $_.Enabled) }
Get-PnpDevice -FriendlyName '*High Precision Event Timer*' -ErrorAction SilentlyContinue | ForEach-Object { $o.Add('hpet.' + $_.InstanceId + '=' + $_.Status) }
Get-PnpDevice -Class Display -ErrorAction SilentlyContinue | ForEach-Object { $k = 'HKLM:\SYSTEM\CurrentControlSet\Enum\' + $_.InstanceId + '\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties'; $v = (Get-ItemProperty -Path $k -Name MSISupported -ErrorAction SilentlyContinue).MSISupported; $o.Add('msi.' + $_.InstanceId + '=' + $v) }
$o | Sort-Object
";
        string text;
        try { text = PowerShellRunner.RunCapture(ps, 120000); }
        catch (Exception ex) { text = "snapshot-error=" + ex.Message; }
        foreach (var line in text.Split('\n'))
        {
            var l = line.TrimEnd('\r');
            int i = l.IndexOf('=');
            if (i > 0) d["PS " + l.Substring(0, i)] = l.Substring(i + 1);
        }
        return d;
    }

    private static string ReadReg(string path, string name)
    {
        var clean = path.Replace(":", "");
        int i = clean.IndexOf('\\');
        var hive = clean.Substring(0, i).ToUpperInvariant();
        var sub = clean.Substring(i + 1);
        var bk = RegistryKey.OpenBaseKey(hive == "HKLM" ? RegistryHive.LocalMachine : hive == "HKCU" ? RegistryHive.CurrentUser : RegistryHive.ClassesRoot, RegistryView.Registry64);
        using (bk)
        using (var k = bk.OpenSubKey(sub))
        {
            if (k == null) return "<nokey>";
            if (!k.GetValueNames().Contains(name)) return "<absent>";
            return $"{k.GetValueKind(name)}:{k.GetValue(name)}";
        }
    }
}
