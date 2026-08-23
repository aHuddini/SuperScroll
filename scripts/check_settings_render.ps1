# Instantiates the settings page in a real visual tree and forces a layout pass.
#
# A green build proves only that the XAML parsed. StaticResource forward references, unresolvable
# template bindings and malformed ControlTemplates all compile cleanly and throw when the page is
# first loaded - which for a settings page means the first time a user opens it. Measure/Arrange is
# what realizes the templates, so running it here surfaces those faults at build time instead.
#
# powershell.exe is STA by default, which WPF requires; no explicit thread juggling needed.

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

$root = Split-Path -Parent $PSScriptRoot
$bin = Join-Path $root 'src\bin\Release\net4.6.2'
if (-not (Test-Path (Join-Path $bin 'SuperScroll.dll'))) { Write-Host 'build first'; exit 1 }

Get-ChildItem "$bin\*.dll" | ForEach-Object { try { [void][Reflection.Assembly]::LoadFrom($_.FullName) } catch {} }
$asm = [Reflection.Assembly]::LoadFrom((Join-Path $bin 'SuperScroll.dll'))

# Stand-in for the view model. It must expose every property the page binds to, not just Settings:
# a WPF binding to a missing property fails SILENTLY and renders empty, so a stub that is missing
# one turns this harness green while the real control would come up blank. Presets in particular
# is what forces the ComboBox to build its item template.
Add-Type -TypeDefinition @'
public class SsHarnessContext {
    public object Settings { get; set; }
    public object Presets { get; set; }
    public object SelectedPreset { get; set; }
    public bool IsRepeatIntervalEnabled { get; set; }
}
'@
$ctx = New-Object SsHarnessContext
$ctx.Settings = $asm.CreateInstance('SuperScroll.SuperScrollSettings')

$presetType = $asm.GetType('SuperScroll.Services.ScrollPreset')
$ctx.Presets = $presetType.GetProperty('AllWithCustom').GetValue($null)
$ctx.SelectedPreset = ($ctx.Presets)[0]
$ctx.IsRepeatIntervalEnabled = $true

$fail = 0
foreach ($name in @('SettingsView')) {
    try {
        $c = $asm.CreateInstance("SuperScroll.Controls.$name")
        if ($null -eq $c) { Write-Host ("  ?? {0,-18} no such type" -f $name); $fail++; continue }
        $c.DataContext = $ctx
        $c.Width = 700; $c.Height = 560
        $c.Measure([Windows.Size]::new(700, 560))
        $c.Arrange([Windows.Rect]::new(0, 0, 700, 560))
        $c.UpdateLayout()

        if ($c.ActualHeight -le 0) { Write-Host ("  XX {0,-18} rendered zero height" -f $name); $fail++; continue }
        Write-Host ("  ok {0,-18} {1:0}x{2:0}" -f $name, $c.ActualWidth, $c.ActualHeight)
    } catch {
        $e = $_.Exception; while ($e.InnerException) { $e = $e.InnerException }
        Write-Host ("  XX {0,-18} {1}" -f $name, $e.Message)
        $fail++
    }
}

Write-Host ''
Write-Host ("pages checked: 1   failures: {0}" -f $fail)
exit $fail
