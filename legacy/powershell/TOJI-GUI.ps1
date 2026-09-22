#Requires -Version 5.1
<#  TOJI-GUI.ps1  -  Suite de sistema v5 (morado, filtros, color por perfil)  #>

$Base = if ($PSScriptRoot) { $PSScriptRoot }
        elseif ($MyInvocation.MyCommand.Path) { Split-Path $MyInvocation.MyCommand.Path }
        else { [System.AppDomain]::CurrentDomain.BaseDirectory }
. (Join-Path $Base 'toji-core.ps1')

if (-not (Test-TojiAdmin)) {
    $self = if ($PSCommandPath) { $PSCommandPath } else { Join-Path $Base 'TOJI-GUI.ps1' }
    Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$self`""
    exit
}
Initialize-Toji
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, Microsoft.VisualBasic, System.Windows.Forms

$tweaks   = Get-TojiTweaks
$apps     = Get-TojiApps
$features = Get-TojiFeatures

[xml]$xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="TOJI - Suite de sistema" Height="860" Width="1000"
        WindowStartupLocation="CenterScreen" Background="#0A0810">
  <Window.Resources>
    <Style TargetType="TextBlock"><Setter Property="Foreground" Value="#E9E6F0"/><Setter Property="FontFamily" Value="Segoe UI"/></Style>
    <Style TargetType="CheckBox"><Setter Property="Foreground" Value="#E9E6F0"/><Setter Property="VerticalAlignment" Value="Center"/></Style>
    <ControlTemplate x:Key="BtnTpl" TargetType="Button">
      <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
              BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}" SnapsToDevicePixels="True">
        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
      </Border>
    </ControlTemplate>
    <Style x:Key="Base" TargetType="Button">
      <Setter Property="Background" Value="#1A1428"/><Setter Property="Foreground" Value="#E9E6F0"/>
      <Setter Property="BorderBrush" Value="#2E2440"/><Setter Property="BorderThickness" Value="1"/>
      <Setter Property="Padding" Value="12,7,12,7"/><Setter Property="Margin" Value="0,0,8,8"/>
      <Setter Property="Cursor" Value="Hand"/><Setter Property="Template" Value="{StaticResource BtnTpl}"/>
      <Style.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="#251B3A"/></Trigger></Style.Triggers>
    </Style>
    <Style x:Key="Primary" TargetType="Button">
      <Setter Property="Background" Value="#8B5CF6"/><Setter Property="Foreground" Value="#140B24"/><Setter Property="FontWeight" Value="Bold"/>
      <Setter Property="BorderBrush" Value="#8B5CF6"/><Setter Property="BorderThickness" Value="1"/>
      <Setter Property="Padding" Value="14,7,14,7"/><Setter Property="Margin" Value="0,0,8,8"/>
      <Setter Property="Cursor" Value="Hand"/><Setter Property="Template" Value="{StaticResource BtnTpl}"/>
      <Style.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="#A78BFA"/></Trigger></Style.Triggers>
    </Style>
    <Style x:Key="Profile" TargetType="Button">
      <Setter Property="Background" Value="#140F1E"/><Setter Property="Foreground" Value="#E9E6F0"/>
      <Setter Property="BorderBrush" Value="#8B5CF6"/><Setter Property="BorderThickness" Value="1"/>
      <Setter Property="Padding" Value="16,8,16,8"/><Setter Property="Margin" Value="0,0,8,0"/>
      <Setter Property="Cursor" Value="Hand"/><Setter Property="FontWeight" Value="SemiBold"/><Setter Property="Template" Value="{StaticResource BtnTpl}"/>
    </Style>
    <Style TargetType="TabItem">
      <Setter Property="Template"><Setter.Value>
        <ControlTemplate TargetType="TabItem">
          <Border x:Name="bd" Background="#140F1E" BorderBrush="#241B33" BorderThickness="1,1,1,0" Padding="18,9,18,9" Margin="0,0,4,0" TextElement.Foreground="#B4ADC7">
            <ContentPresenter ContentSource="Header" HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsSelected" Value="True">
              <Setter TargetName="bd" Property="Background" Value="#1B1430"/><Setter TargetName="bd" Property="BorderBrush" Value="#8B5CF6"/><Setter TargetName="bd" Property="TextElement.Foreground" Value="#A78BFA"/>
            </Trigger>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="bd" Property="Background" Value="#1A1330"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value></Setter>
      <Setter Property="FontWeight" Value="SemiBold"/>
    </Style>
  </Window.Resources>

  <Grid Margin="18">
    <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/></Grid.RowDefinitions>
    <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,12">
      <TextBlock Text="TOJI" FontSize="34" FontWeight="Bold" Foreground="#A78BFA"/>
      <TextBlock Text="Restriccion Celestial" FontSize="13" Foreground="#8A83A0" VerticalAlignment="Bottom" Margin="12,0,0,7"/>
    </StackPanel>

    <TabControl Grid.Row="1" Background="#0A0810" BorderThickness="0" Padding="0,8,0,0">
      <TabItem Header="Optimizaciones">
        <Grid Background="#0A0810" Margin="0,10,0,0">
          <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/>
          </Grid.RowDefinitions>
          <Border Grid.Row="0" Background="#120D1C" BorderBrush="#241B33" BorderThickness="1" Padding="12">
            <StackPanel>
              <TextBlock Text="PERFILES" Foreground="#8A83A0" FontSize="11" Margin="0,0,0,8"/>
              <WrapPanel x:Name="ProfileBar"/>
              <TextBlock x:Name="ProfileLabel" Text="Perfil: ninguno (seleccion manual)" Foreground="#8A83A0" FontSize="12" Margin="0,10,0,0" TextWrapping="Wrap"/>
            </StackPanel>
          </Border>
          <DockPanel Grid.Row="1" Margin="0,12,0,6">
            <TextBlock Text="Buscar:" Foreground="#8A83A0" VerticalAlignment="Center" Margin="0,0,8,0" DockPanel.Dock="Left"/>
            <TextBox x:Name="SearchBox" DockPanel.Dock="Left" Width="220" Background="#171122" Foreground="#E9E6F0" BorderBrush="#2E2440" Padding="6,4,6,4"/>
            <TextBlock x:Name="CountText" Text="0 seleccionados" Foreground="#A78BFA" VerticalAlignment="Center" HorizontalAlignment="Right"/>
          </DockPanel>
          <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,0,0,8">
            <TextBlock Text="Riesgo:" Foreground="#8A83A0" VerticalAlignment="Center" Margin="0,0,8,0"/>
            <WrapPanel x:Name="RiskChips" VerticalAlignment="Center"/>
            <TextBlock Text="Categoria:" Foreground="#8A83A0" VerticalAlignment="Center" Margin="16,0,8,0"/>
            <ComboBox x:Name="CatCombo" Width="200" Height="26" VerticalAlignment="Center" Background="#171122" Foreground="#201020"/>
          </StackPanel>
          <Border Grid.Row="3" BorderBrush="#241B33" BorderThickness="1" Background="#100B18">
            <ScrollViewer VerticalScrollBarVisibility="Auto"><StackPanel x:Name="RootList" Margin="8"/></ScrollViewer>
          </Border>
          <StackPanel Grid.Row="4" Margin="2,10,2,6">
            <TextBlock x:Name="StatusText" Foreground="#8A83A0" TextWrapping="Wrap" Text="Al abrir se marca lo que ya tienes aplicado. Crea un punto de restauracion antes de tocar."/>
            <ProgressBar x:Name="Progress" Height="6" Margin="0,8,0,0" Minimum="0" Maximum="100" Value="0" Visibility="Collapsed" Background="#171122" Foreground="#8B5CF6" BorderThickness="0"/>
          </StackPanel>
          <WrapPanel Grid.Row="5">
            <Button x:Name="BtnApply" Style="{StaticResource Primary}" Content="APLICAR (dejar exacto lo marcado)"/>
            <Button x:Name="BtnDry" Style="{StaticResource Base}" Content="Simular"/>
            <Button x:Name="BtnSaveProfile" Style="{StaticResource Base}" Content="Guardar perfil actual"/>
            <Button x:Name="BtnRevertAll" Style="{StaticResource Base}" Foreground="#CF4A4F" Content="Revertir TODO"/>
            <Button x:Name="BtnRestore" Style="{StaticResource Base}" Content="Punto de restauracion"/>
            <Button x:Name="BtnExport" Style="{StaticResource Base}" Content="Exportar codigo"/>
            <Button x:Name="BtnImport" Style="{StaticResource Base}" Content="Importar codigo"/>
            <Button x:Name="BtnClean" Style="{StaticResource Base}" Content="Limpiar basura"/>
          </WrapPanel>
        </Grid>
      </TabItem>

      <TabItem Header="Apps (Winget)">
        <Grid Background="#0A0810" Margin="0,10,0,0">
          <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
          <DockPanel Grid.Row="0" Margin="0,0,0,8">
            <TextBlock x:Name="WingetNote" Foreground="#8A83A0" FontSize="12" DockPanel.Dock="Left" Text="Marca las apps y pulsa Instalar. Se abrira una ventana con el progreso."/>
            <TextBlock x:Name="AppCount" Text="0 seleccionadas" Foreground="#A78BFA" VerticalAlignment="Center" HorizontalAlignment="Right"/>
          </DockPanel>
          <Border Grid.Row="1" BorderBrush="#241B33" BorderThickness="1" Background="#100B18">
            <ScrollViewer VerticalScrollBarVisibility="Auto"><StackPanel x:Name="AppList" Margin="8"/></ScrollViewer>
          </Border>
          <TextBlock x:Name="AppStatus" Grid.Row="2" Margin="2,12,2,8" Foreground="#8A83A0" TextWrapping="Wrap" Text=""/>
          <WrapPanel Grid.Row="3">
            <Button x:Name="BtnInstall" Style="{StaticResource Primary}" Content="Instalar seleccionadas"/>
            <Button x:Name="BtnUninstall" Style="{StaticResource Base}" Content="Desinstalar seleccionadas"/>
            <Button x:Name="BtnUpgrade" Style="{StaticResource Base}" Content="Actualizar todo (winget upgrade)"/>
            <Button x:Name="BtnGpu" Style="{StaticResource Base}" Content="Detectar GPU + drivers"/>
          </WrapPanel>
        </Grid>
      </TabItem>

      <TabItem Header="Reparaciones">
        <Grid Background="#0A0810" Margin="0,10,0,0">
          <Grid.RowDefinitions><RowDefinition Height="*"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
          <ScrollViewer Grid.Row="0" VerticalScrollBarVisibility="Auto">
            <StackPanel Margin="2">
              <StackPanel Orientation="Horizontal" Margin="0,4,0,6"><Border Background="#8B5CF6" Width="3" Height="16" Margin="0,0,8,0"/><TextBlock Text="BASICAS" FontWeight="Bold" FontSize="14" VerticalAlignment="Center"/></StackPanel>
              <WrapPanel Margin="0,0,0,4">
                <Button x:Name="BtnClean2" Style="{StaticResource Base}" Content="Limpiar basura (temp/papelera)"/>
                <Button x:Name="BtnFlushDns" Style="{StaticResource Base}" Content="Vaciar cache DNS"/>
              </WrapPanel>
              <TextBlock Text="Rapidas y sin riesgo." Foreground="#8A83A0" FontSize="12" Margin="0,0,0,16"/>
              <StackPanel Orientation="Horizontal" Margin="0,4,0,6"><Border Background="#BD9140" Width="3" Height="16" Margin="0,0,8,0"/><TextBlock Text="AVANZADAS" FontWeight="Bold" FontSize="14" VerticalAlignment="Center"/></StackPanel>
              <WrapPanel Margin="0,0,0,4">
                <Button x:Name="BtnSFC" Style="{StaticResource Base}" Content="SFC (reparar archivos)"/>
                <Button x:Name="BtnDISM" Style="{StaticResource Base}" Content="DISM RestoreHealth"/>
                <Button x:Name="BtnNet" Style="{StaticResource Base}" Content="Reset de red completo"/>
                <Button x:Name="BtnWU" Style="{StaticResource Base}" Content="Reparar Windows Update"/>
                <Button x:Name="BtnChkdsk" Style="{StaticResource Base}" Content="Escanear disco (CHKDSK)"/>
              </WrapPanel>
              <TextBlock Text="Cada una abre su ventana con el progreso. SFC y DISM tardan varios minutos." Foreground="#8A83A0" FontSize="12" TextWrapping="Wrap"/>
            </StackPanel>
          </ScrollViewer>
          <TextBlock x:Name="RepairStatus" Grid.Row="1" Margin="2,10,2,4" Foreground="#8A83A0" TextWrapping="Wrap" Text=""/>
        </Grid>
      </TabItem>

      <TabItem Header="Caracteristicas de Windows">
        <Grid Background="#0A0810" Margin="0,10,0,0">
          <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
          <TextBlock Grid.Row="0" Margin="0,0,0,8" Foreground="#8A83A0" FontSize="12" TextWrapping="Wrap" Text="Marca las que quieras y pulsa Activar o Desactivar. 'Refrescar estado' muestra como estan. Suelen pedir reiniciar."/>
          <Border Grid.Row="1" BorderBrush="#241B33" BorderThickness="1" Background="#100B18">
            <ScrollViewer VerticalScrollBarVisibility="Auto"><StackPanel x:Name="FeatureList" Margin="8"/></ScrollViewer>
          </Border>
          <TextBlock x:Name="FeatStatus" Grid.Row="2" Margin="2,12,2,8" Foreground="#8A83A0" TextWrapping="Wrap" Text=""/>
          <WrapPanel Grid.Row="3">
            <Button x:Name="BtnFeatEnable" Style="{StaticResource Primary}" Content="Activar seleccionadas"/>
            <Button x:Name="BtnFeatDisable" Style="{StaticResource Base}" Content="Desactivar seleccionadas"/>
            <Button x:Name="BtnFeatRefresh" Style="{StaticResource Base}" Content="Refrescar estado"/>
          </WrapPanel>
        </Grid>
      </TabItem>
    </TabControl>
  </Grid>
</Window>
'@

$reader = New-Object System.Xml.XmlNodeReader $xaml
$win = [Windows.Markup.XamlReader]::Load($reader)

$dangerColor = @{ safe='#3FB982'; moderate='#59C3D6'; high='#BD9140'; extreme='#CF4A4F' }
$riskColor   = @{ todos='#8A83A0'; safe='#3FB982'; moderate='#59C3D6'; high='#BD9140'; extreme='#CF4A4F' }
$profColors  = @{ competitivo='#8B5CF6'; ciber='#59C3D6'; limpio='#8A83A0' }
$accent='#8B5CF6'
function B($hex){ New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.ColorConverter]::ConvertFromString($hex)) }
function TB($text,$hex,$size,$bold,$wrap){ $t=New-Object System.Windows.Controls.TextBlock; $t.Text=$text; $t.Foreground=B $hex; if($size){$t.FontSize=$size}; if($bold){$t.FontWeight='Bold'}; if($wrap){$t.TextWrapping='Wrap'}; $t }

# ---- TAB 1 refs/estado ----
$script:RootList=$win.FindName('RootList'); $script:CountText=$win.FindName('CountText')
$script:StatusText=$win.FindName('StatusText'); $script:ProfileLabel=$win.FindName('ProfileLabel'); $script:Progress=$win.FindName('Progress')
$SearchBox=$win.FindName('SearchBox'); $ProfileBar=$win.FindName('ProfileBar')
$script:RiskChips=$win.FindName('RiskChips'); $CatCombo=$win.FindName('CatCombo')
$script:CheckBoxes=@{}; $script:Rows=@(); $script:Heads=@()
$script:FDanger='todos'; $script:FCat='todas'

function Update-Count { $n=($script:CheckBoxes.Values|Where-Object{$_.IsChecked}).Count; $a=(Get-TojiApplied).Count; $script:CountText.Text="$n seleccionados  |  $a aplicados" }
function Get-CheckedIds { $script:CheckBoxes.GetEnumerator()|Where-Object{$_.Value.IsChecked}|ForEach-Object{$_.Key} }
function Select-Profile($p){
    $ids=@($p.tweaks); foreach($kv in $script:CheckBoxes.GetEnumerator()){$kv.Value.IsChecked=($ids -contains $kv.Key)}
    $col=$profColors[$p.id]; if(-not $col){$col='#A78BFA'}
    foreach($b in $ProfileBar.Children){ if($b.Tag -and $b.Tag.id -eq $p.id){ $b.Background=B $col; $b.Foreground=B '#140B24' } else { $b.Background=B '#140F1E'; $b.Foreground=B '#E9E6F0' } }
    $script:ProfileLabel.Text="Perfil: $($p.name)  -  $($p.desc)"; $script:ProfileLabel.Foreground=B $col
    Update-Count
}
function Build-ProfileBar {
    $ProfileBar.Children.Clear(); $script:profiles=Get-TojiProfiles
    foreach($p in $script:profiles){ $pb=New-Object System.Windows.Controls.Button; $pb.Content=$p.name; $pb.Style=$win.FindResource('Profile'); $pb.Tag=$p; $pb.Add_Click({param($s,$e) Select-Profile $s.Tag}); [void]$ProfileBar.Children.Add($pb) }
}
function Apply-Filter {
    $q=$SearchBox.Text.ToLower().Trim()
    foreach($r in $script:Rows){
        $okD=($script:FDanger -eq 'todos') -or ($r.Danger -eq $script:FDanger)
        $okC=($script:FCat -eq 'todas') -or ($r.Cat -eq $script:FCat)
        $okS=($q -eq '') -or $r.Text.Contains($q)
        if($okD -and $okC -and $okS){$r.Border.Visibility='Visible'}else{$r.Border.Visibility='Collapsed'}
    }
    foreach($h in $script:Heads){ $vis=$false; foreach($r in $script:Rows){ if($r.Cat -eq $h.Cat -and $r.Border.Visibility -eq 'Visible'){$vis=$true;break} }; $h.Panel.Visibility= if($vis){'Visible'}else{'Collapsed'} }
}
function Set-RiskFilter($rk){ $script:FDanger=$rk; foreach($kv in $script:RiskBtns.GetEnumerator()){ if($kv.Key -eq $rk){$kv.Value.BorderBrush=B $riskColor[$rk]}else{$kv.Value.BorderBrush=B '#2E2440'} }; Apply-Filter }

# trabajo con barra de progreso (sin congelar)
function Set-UIBusy($busy){ foreach($n in 'BtnApply','BtnDry','BtnRevertAll','BtnRestore','BtnExport','BtnImport','BtnClean','BtnSaveProfile'){ $b=$win.FindName($n); if($b){$b.IsEnabled=-not $busy} }; $script:Progress.Visibility= if($busy){'Visible'}else{'Collapsed'}; if(-not $busy){$script:Progress.Value=0} }
function Invoke-WithProgress { param([object[]]$Items,[scriptblock]$Step,[string]$Verb='Procesando')
    Set-UIBusy $true; $total=[Math]::Max($Items.Count,1); $i=0
    foreach($it in $Items){ $i++; $script:StatusText.Text="$Verb ($i/$total): $it"; $script:Progress.Value=[int](($i/$total)*100); [System.Windows.Forms.Application]::DoEvents(); & $Step $it }
    Set-UIBusy $false
}

# ---- construir lista por categoria ----
$catOrder='Rendimiento','Input','Energia','Red','GPU','Interfaz','Privacidad','Almacenamiento','Debloat','Activacion','Windows Update','Seguridad','PROHIBIDO'
$cats=@(); foreach($c in $catOrder){ if($tweaks|Where-Object{$_.category -eq $c}){$cats+=$c} }
foreach($c in ($tweaks.category|Select-Object -Unique)){ if($cats -notcontains $c){$cats+=$c} }
foreach($c in $cats){
    $group=$tweaks|Where-Object{$_.category -eq $c}; if(-not $group){continue}
    $head=New-Object System.Windows.Controls.StackPanel; $head.Orientation='Horizontal'; $head.Margin='2,14,0,6'
    $bar=New-Object System.Windows.Controls.Border; $bar.Background=B $accent; $bar.Width=3; $bar.Height=16; $bar.Margin='0,0,8,0'
    $ht=TB $c.ToUpper() '#E9E6F0' 14 $true $false; $ht.VerticalAlignment='Center'
    [void]$head.Children.Add($bar);[void]$head.Children.Add($ht);[void]$script:RootList.Children.Add($head)
    $script:Heads+=[pscustomobject]@{Panel=$head;Cat=$c}
    foreach($t in $group){
        $border=New-Object System.Windows.Controls.Border; $border.Background=B '#171122'; $border.Margin='0,1,0,0'; $border.Padding='10,8,10,8'
        $grid=New-Object System.Windows.Controls.Grid
        $c0=New-Object System.Windows.Controls.ColumnDefinition; $c0.Width=[System.Windows.GridLength]::Auto
        $c1=New-Object System.Windows.Controls.ColumnDefinition; $c1.Width=New-Object System.Windows.GridLength(1,[System.Windows.GridUnitType]::Star)
        $c2=New-Object System.Windows.Controls.ColumnDefinition; $c2.Width=[System.Windows.GridLength]::Auto
        $grid.ColumnDefinitions.Add($c0);$grid.ColumnDefinitions.Add($c1);$grid.ColumnDefinitions.Add($c2)
        $cb=New-Object System.Windows.Controls.CheckBox; $cb.Margin='0,0,12,0'; $cb.Tag=$t.id
        $cb.Add_Checked({Update-Count}); $cb.Add_Unchecked({Update-Count}); [System.Windows.Controls.Grid]::SetColumn($cb,0); $script:CheckBoxes[$t.id]=$cb
        $sp=New-Object System.Windows.Controls.StackPanel; [System.Windows.Controls.Grid]::SetColumn($sp,1)
        [void]$sp.Children.Add((TB $t.name '#E9E6F0' $null $true $true))
        $de=TB $t.desc '#8A83A0' 12 $false $true; $de.Margin='0,2,0,0'; [void]$sp.Children.Add($de)
        if($t.warning){ $w=TB ("! "+$t.warning) '#BD9140' 11 $false $true; $w.Margin='0,3,0,0'; [void]$sp.Children.Add($w) }
        if($t.anticheat){ $ac=TB "* riesgo anti-cheat" '#CF4A4F' 11 $false $false; $ac.Margin='0,3,0,0'; [void]$sp.Children.Add($ac) }
        $dp=New-Object System.Windows.Controls.StackPanel; $dp.Orientation='Horizontal'; $dp.VerticalAlignment='Top'; $dp.Margin='10,2,0,0'; [System.Windows.Controls.Grid]::SetColumn($dp,2)
        $dot=New-Object System.Windows.Shapes.Ellipse; $dot.Width=9; $dot.Height=9; $dot.Fill=B $dangerColor[$t.danger]; $dot.VerticalAlignment='Center'; $dot.Margin='0,0,5,0'
        [void]$dp.Children.Add($dot);[void]$dp.Children.Add((TB $t.danger $dangerColor[$t.danger] 11 $false $false))
        [void]$grid.Children.Add($cb);[void]$grid.Children.Add($sp);[void]$grid.Children.Add($dp)
        $border.Child=$grid; [void]$script:RootList.Children.Add($border)
        $script:Rows+=[pscustomobject]@{Border=$border;Text=("$($t.name) $($t.desc) $($t.id) $($t.category)").ToLower();Danger=$t.danger;Cat=$c}
    }
}
Build-ProfileBar

# chips de riesgo
$script:RiskBtns=@{}
foreach($rk in 'todos','safe','moderate','high','extreme'){
    $chip=New-Object System.Windows.Controls.Button; $chip.Content=$rk; $chip.Style=$win.FindResource('Base'); $chip.Tag=$rk
    $chip.FontSize=11; $chip.Padding='8,3,8,3'; $chip.Margin='0,0,6,0'; $chip.Foreground=B $riskColor[$rk]
    $chip.Add_Click({param($s,$e) Set-RiskFilter $s.Tag}); $script:RiskBtns[$rk]=$chip; [void]$script:RiskChips.Children.Add($chip)
}
$script:RiskBtns['todos'].BorderBrush=B $riskColor['todos']
# combo de categoria
[void]$CatCombo.Items.Add('todas'); foreach($c in $cats){ [void]$CatCombo.Items.Add($c) }; $CatCombo.SelectedIndex=0
$CatCombo.Add_SelectionChanged({ $script:FCat=[string]$CatCombo.SelectedItem; Apply-Filter })

# marcar lo ya aplicado
foreach($id in @(Get-TojiApplied)){ if($script:CheckBoxes.ContainsKey($id)){ $script:CheckBoxes[$id].IsChecked=$true } }
$SearchBox.Add_TextChanged({ Apply-Filter })

$win.FindName('BtnApply').Add_Click({
    $ids=@(Get-CheckedIds); $applied=@(Get-TojiApplied)
    $ext=$tweaks|Where-Object{$ids -contains $_.id -and $applied -notcontains $_.id -and $_.danger -eq 'extreme'}
    if($ext){ $m="Vas a aplicar ajustes PROHIBIDOS:`n`n"+(($ext|ForEach-Object{"- $($_.name)"}) -join "`n")+"`n`nContinuar?"; if([System.Windows.MessageBox]::Show($m,'TOJI - PELIGRO',[System.Windows.MessageBoxButton]::YesNo,[System.Windows.MessageBoxImage]::Warning) -ne 'Yes'){return} }
    $toRevert=@($applied|Where-Object{$ids -notcontains $_}); $toApply=@($ids|Where-Object{$applied -notcontains $_})
    $script:Errs=@(); $script:Reboot=$false
    Invoke-WithProgress -Items $toRevert -Verb 'Revirtiendo' -Step { param($id) $r=Revert-Tweak -Id $id; if($r.Errors){$script:Errs+=$r.Errors} }
    Invoke-WithProgress -Items $toApply -Verb 'Aplicando' -Step { param($id) $t=$tweaks|Where-Object id -eq $id; $r=Apply-Tweak -Tweak $t; if($r.Errors){$script:Errs+=$r.Errors}; if($t.reboot){$script:Reboot=$true} }
    Update-Count
    $msg="Hecho. Aplicados: $($toApply.Count) | Revertidos: $($toRevert.Count)."; if($script:Reboot){$msg+="`nAlgunos cambios necesitan REINICIAR."}; if($script:Errs){$msg+="`n`nAvisos:`n"+($script:Errs -join "`n")}
    $script:StatusText.Text='Listo.'; [System.Windows.MessageBox]::Show($msg,'TOJI')|Out-Null
})
$win.FindName('BtnDry').Add_Click({
    $ids=@(Get-CheckedIds); $applied=@(Get-TojiApplied)
    $toRevert=@($applied|Where-Object{$ids -notcontains $_}); $toApply=@($ids|Where-Object{$applied -notcontains $_})
    $m="SIMULACION (no se toco nada).`n`nSe APLICARIAN ("+$toApply.Count+"):`n"+(($toApply|ForEach-Object{"  + $_"}) -join "`n")+"`n`nSe REVERTIRIAN ("+$toRevert.Count+"):`n"+(($toRevert|ForEach-Object{"  - $_"}) -join "`n")
    [System.Windows.MessageBox]::Show($m,'TOJI - dry run')|Out-Null
})
$win.FindName('BtnSaveProfile').Add_Click({
    $ids=@(Get-CheckedIds); if(-not $ids){[System.Windows.MessageBox]::Show('Marca primero los ajustes que quieras en el perfil.','TOJI')|Out-Null;return}
    $name=[Microsoft.VisualBasic.Interaction]::InputBox('Nombre del perfil:','Guardar perfil','Mi setup'); if(-not $name){return}
    Save-TojiProfile -Name $name -Ids $ids | Out-Null; Build-ProfileBar
    [System.Windows.MessageBox]::Show("Perfil '$name' guardado. Ya aparece arriba.",'TOJI')|Out-Null
})
$win.FindName('BtnRevertAll').Add_Click({ if([System.Windows.MessageBox]::Show('Revertir TODO lo aplicado por TOJI?','TOJI',[System.Windows.MessageBoxButton]::YesNo) -ne 'Yes'){return}; Revert-All; foreach($kv in $script:CheckBoxes.GetEnumerator()){$kv.Value.IsChecked=$false}; foreach($b in $ProfileBar.Children){$b.Background=B '#140F1E';$b.Foreground=B '#E9E6F0'}; $script:ProfileLabel.Text='Perfil: ninguno'; $script:ProfileLabel.Foreground=B '#8A83A0'; Update-Count; [System.Windows.MessageBox]::Show('Todo revertido. Reinicia para que asiente.','TOJI')|Out-Null })
$win.FindName('BtnRestore').Add_Click({ $script:StatusText.Text='Creando punto de restauracion...'; if(New-TojiRestorePoint){[System.Windows.MessageBox]::Show('Punto de restauracion creado.','TOJI')|Out-Null}else{[System.Windows.MessageBox]::Show('No se pudo crear. Restaurar sistema puede estar desactivado.','TOJI')|Out-Null}; $script:StatusText.Text='Listo.' })
$win.FindName('BtnExport').Add_Click({ $ids=@(Get-CheckedIds); $code=Export-TojiCode -Ids ($(if($ids){$ids}else{$null})); Set-Clipboard $code -ErrorAction SilentlyContinue; [System.Windows.MessageBox]::Show("Codigo (copiado):`n`n$code",'TOJI - exportar')|Out-Null })
$win.FindName('BtnImport').Add_Click({ $code=[Microsoft.VisualBasic.Interaction]::InputBox('Pega el codigo TOJI:','Importar codigo',''); if(-not $code){return}; try{ $ids=Import-TojiCode $code; foreach($kv in $script:CheckBoxes.GetEnumerator()){$kv.Value.IsChecked=($ids -contains $kv.Key)}; $script:ProfileLabel.Text='Perfil: importado desde codigo'; Update-Count; [System.Windows.MessageBox]::Show("$($ids.Count) ajustes marcados. Pulsa APLICAR.",'TOJI')|Out-Null }catch{[System.Windows.MessageBox]::Show('Codigo invalido.','TOJI')|Out-Null} })
$win.FindName('BtnClean').Add_Click({ $script:StatusText.Text='Limpiando...'; Clear-TojiTemp; $script:StatusText.Text='Basura eliminada.'; [System.Windows.MessageBox]::Show('Basura eliminada.','TOJI')|Out-Null })

# ---- TAB 2 APPS ----
$script:AppList=$win.FindName('AppList'); $script:AppCount=$win.FindName('AppCount'); $script:AppStatus=$win.FindName('AppStatus')
$script:AppBoxes=@{}
function Update-AppCount { $n=($script:AppBoxes.Values|Where-Object{$_.IsChecked}).Count; $script:AppCount.Text="$n seleccionadas" }
function Get-CheckedApps { $script:AppBoxes.GetEnumerator()|Where-Object{$_.Value.IsChecked}|ForEach-Object{$_.Key} }
$appCatOrder='Navegadores','Comunicacion','Gaming','Desarrollo','Utilidades','Multimedia','Ciberseguridad'
$acats=@(); foreach($c in $appCatOrder){ if($apps|Where-Object{$_.category -eq $c}){$acats+=$c} }
foreach($c in ($apps.category|Select-Object -Unique)){ if($acats -notcontains $c){$acats+=$c} }
foreach($c in $acats){
    $group=$apps|Where-Object{$_.category -eq $c}; if(-not $group){continue}
    $head=New-Object System.Windows.Controls.StackPanel; $head.Orientation='Horizontal'; $head.Margin='2,14,0,6'
    $bar=New-Object System.Windows.Controls.Border; $bar.Background=B $accent; $bar.Width=3; $bar.Height=16; $bar.Margin='0,0,8,0'
    [void]$head.Children.Add($bar);[void]$head.Children.Add((TB $c.ToUpper() '#E9E6F0' 14 $true $false)); [void]$script:AppList.Children.Add($head)
    foreach($a in $group){
        $border=New-Object System.Windows.Controls.Border; $border.Background=B '#171122'; $border.Margin='0,1,0,0'; $border.Padding='10,8,10,8'
        $cb=New-Object System.Windows.Controls.CheckBox; $cb.Tag=$a.id; $cb.Add_Checked({Update-AppCount}); $cb.Add_Unchecked({Update-AppCount}); $script:AppBoxes[$a.id]=$cb
        $sp=New-Object System.Windows.Controls.StackPanel; $sp.Margin='10,0,0,0'
        [void]$sp.Children.Add((TB $a.name '#E9E6F0' $null $true $false)); $d=TB $a.desc '#8A83A0' 12 $false $true; $d.Margin='0,2,0,0'; [void]$sp.Children.Add($d)
        $row=New-Object System.Windows.Controls.StackPanel; $row.Orientation='Horizontal'; [void]$row.Children.Add($cb); [void]$row.Children.Add($sp); $border.Child=$row; [void]$script:AppList.Children.Add($border)
    }
}
if(-not (Test-TojiWinget)){ $win.FindName('WingetNote').Text='winget no disponible. Instala "App Installer" desde Microsoft Store.'; $win.FindName('WingetNote').Foreground=B '#CF4A4F'; foreach($n in 'BtnInstall','BtnUninstall','BtnUpgrade'){$win.FindName($n).IsEnabled=$false} }
$win.FindName('BtnInstall').Add_Click({ $ids=@(Get-CheckedApps); if(-not $ids){[System.Windows.MessageBox]::Show('No hay apps marcadas.','TOJI')|Out-Null;return}; Install-TojiApps -Ids $ids; $script:AppStatus.Text="Instalando $($ids.Count) apps en una ventana aparte..." })
$win.FindName('BtnUninstall').Add_Click({ $ids=@(Get-CheckedApps); if(-not $ids){[System.Windows.MessageBox]::Show('No hay apps marcadas.','TOJI')|Out-Null;return}; if([System.Windows.MessageBox]::Show("Desinstalar $($ids.Count) apps?",'TOJI',[System.Windows.MessageBoxButton]::YesNo) -ne 'Yes'){return}; Uninstall-TojiApps -Ids $ids; $script:AppStatus.Text="Desinstalando en una ventana aparte..." })
$win.FindName('BtnUpgrade').Add_Click({ Start-TojiConsole -Title 'winget upgrade' -Command 'winget upgrade --all --accept-source-agreements --accept-package-agreements'; $script:AppStatus.Text="Actualizando todo en una ventana aparte..." })
$win.FindName('BtnGpu').Add_Click({ $r=Open-TojiGpuDriver; if($r.Url){ [System.Windows.MessageBox]::Show("GPU detectada:`n$($r.Gpu)`n`nAbriendo la pagina oficial de drivers. winget no instala drivers de GPU, por eso se descargan de la web del fabricante.",'TOJI')|Out-Null } else { [System.Windows.MessageBox]::Show("No pude identificar el fabricante de la GPU:`n$($r.Gpu)",'TOJI')|Out-Null } })

# ---- TAB 3 REPARACIONES ----
$script:RepairStatus=$win.FindName('RepairStatus')
$win.FindName('BtnClean2').Add_Click({ Clear-TojiTemp; $script:RepairStatus.Text='Basura eliminada.' })
$win.FindName('BtnFlushDns').Add_Click({ Repair-TojiFlushDNS; $script:RepairStatus.Text='Cache DNS vaciada.' })
$win.FindName('BtnSFC').Add_Click({ Repair-TojiSFC; $script:RepairStatus.Text='SFC en marcha (ventana aparte).' })
$win.FindName('BtnDISM').Add_Click({ Repair-TojiDISM; $script:RepairStatus.Text='DISM en marcha (ventana aparte).' })
$win.FindName('BtnNet').Add_Click({ if([System.Windows.MessageBox]::Show('Reset de red completo? Reinicia despues.','TOJI',[System.Windows.MessageBoxButton]::YesNo) -ne 'Yes'){return}; Repair-TojiNetwork; $script:RepairStatus.Text='Reset de red en marcha (ventana aparte).' })
$win.FindName('BtnWU').Add_Click({ Repair-TojiWU; $script:RepairStatus.Text='Reparando Windows Update (ventana aparte).' })
$win.FindName('BtnChkdsk').Add_Click({ Repair-TojiChkdsk; $script:RepairStatus.Text='Escaneando disco (ventana aparte).' })

# ---- TAB 4 CARACTERISTICAS ----
$script:FeatureList=$win.FindName('FeatureList'); $script:FeatStatus=$win.FindName('FeatStatus')
$script:FeatBoxes=@{}; $script:FeatState=@{}
foreach($f in $features){
    $border=New-Object System.Windows.Controls.Border; $border.Background=B '#171122'; $border.Margin='0,1,0,0'; $border.Padding='10,8,10,8'
    $grid=New-Object System.Windows.Controls.Grid
    $c0=New-Object System.Windows.Controls.ColumnDefinition; $c0.Width=[System.Windows.GridLength]::Auto
    $c1=New-Object System.Windows.Controls.ColumnDefinition; $c1.Width=New-Object System.Windows.GridLength(1,[System.Windows.GridUnitType]::Star)
    $c2=New-Object System.Windows.Controls.ColumnDefinition; $c2.Width=[System.Windows.GridLength]::Auto
    $grid.ColumnDefinitions.Add($c0);$grid.ColumnDefinitions.Add($c1);$grid.ColumnDefinitions.Add($c2)
    $cb=New-Object System.Windows.Controls.CheckBox; $cb.Margin='0,0,12,0'; $cb.Tag=$f.feature; [System.Windows.Controls.Grid]::SetColumn($cb,0); $script:FeatBoxes[$f.feature]=$cb
    $sp=New-Object System.Windows.Controls.StackPanel; [System.Windows.Controls.Grid]::SetColumn($sp,1)
    [void]$sp.Children.Add((TB $f.name '#E9E6F0' $null $true $true)); $d=TB $f.desc '#8A83A0' 12 $false $true; $d.Margin='0,2,0,0'; [void]$sp.Children.Add($d)
    $st=TB 'estado: ?' '#8A83A0' 11 $false $false; $st.VerticalAlignment='Top'; $st.Margin='10,2,0,0'; [System.Windows.Controls.Grid]::SetColumn($st,2); $script:FeatState[$f.feature]=$st
    [void]$grid.Children.Add($cb);[void]$grid.Children.Add($sp);[void]$grid.Children.Add($st); $border.Child=$grid; [void]$script:FeatureList.Children.Add($border)
}
function Refresh-Features { foreach($kv in $script:FeatState.GetEnumerator()){ $s=Get-TojiFeatureState -Name $kv.Key; $kv.Value.Text="estado: $s"; if($s -eq 'Enabled'){$kv.Value.Foreground=B '#3FB982'}elseif($s -eq 'Disabled'){$kv.Value.Foreground=B '#8A83A0'}else{$kv.Value.Foreground=B '#BD9140'} } }
$win.FindName('BtnFeatRefresh').Add_Click({ $script:FeatStatus.Text='Consultando...'; Refresh-Features; $script:FeatStatus.Text='Estado actualizado.' })
$win.FindName('BtnFeatEnable').Add_Click({ $sel=$script:FeatBoxes.GetEnumerator()|Where-Object{$_.Value.IsChecked}|ForEach-Object{$_.Key}; if(-not $sel){[System.Windows.MessageBox]::Show('No hay caracteristicas marcadas.','TOJI')|Out-Null;return}; $err=@(); foreach($n in $sel){ try{ Set-TojiFeature -Name $n -Enable $true }catch{ $err+="$n : $($_.Exception.Message)" } }; Refresh-Features; $m="Activadas: $($sel.Count - $err.Count). Reinicia para completar."; if($err){$m+=" Errores: "+($err -join '; ')}; $script:FeatStatus.Text=$m; [System.Windows.MessageBox]::Show($m,'TOJI')|Out-Null })
$win.FindName('BtnFeatDisable').Add_Click({ $sel=$script:FeatBoxes.GetEnumerator()|Where-Object{$_.Value.IsChecked}|ForEach-Object{$_.Key}; if(-not $sel){[System.Windows.MessageBox]::Show('No hay caracteristicas marcadas.','TOJI')|Out-Null;return}; $err=@(); foreach($n in $sel){ try{ Set-TojiFeature -Name $n -Enable $false }catch{ $err+="$n : $($_.Exception.Message)" } }; Refresh-Features; $m="Desactivadas: $($sel.Count - $err.Count). Reinicia para completar."; if($err){$m+=" Errores: "+($err -join '; ')}; $script:FeatStatus.Text=$m; [System.Windows.MessageBox]::Show($m,'TOJI')|Out-Null })

Update-Count; Update-AppCount; Apply-Filter
$win.ShowDialog() | Out-Null
