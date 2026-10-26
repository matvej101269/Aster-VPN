param([switch]$Settings)
# Render XAML in isolation: no application code, saved settings or VPN startup.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$root = Split-Path $PSScriptRoot -Parent
$appXaml = [xml](Get-Content -LiteralPath (Join-Path $root 'src/App.xaml') -Raw -Encoding utf8)
$app = New-Object System.Windows.Application
$dictionary = '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">' + $appXaml.DocumentElement.FirstChild.InnerXml + '</ResourceDictionary>'
$app.Resources = [Windows.Markup.XamlReader]::Parse($dictionary)
$windowXaml = [xml](Get-Content -LiteralPath (Join-Path $root 'src/MainWindow.xaml') -Raw -Encoding utf8)
foreach ($element in $windowXaml.SelectNodes('//*')) {
    foreach ($attribute in @($element.Attributes)) {
        if ($attribute.LocalName -in 'Class','Click','Checked','Unchecked','Expanded','Collapsed','Loaded','Closing','SelectionChanged','TextChanged','MouseDoubleClick','PreviewMouseWheel') {
            [void]$element.RemoveAttributeNode($attribute)
        }
    }
    if ($element.LocalName -eq 'Image' -and $element.GetAttribute('Source')) {
        $element.SetAttribute('Source', (Join-Path $root ('src/' + $element.GetAttribute('Source'))))
    }
}
$windowXaml.DocumentElement.RemoveAttribute('Icon')
$window = [Windows.Markup.XamlReader]::Parse($windowXaml.OuterXml)
$window.FindName('Dashboard').Visibility = 'Collapsed'
$window.FindName('RoutingPage').Visibility = if ($Settings) { 'Collapsed' } else { 'Visible' }
$window.FindName('SettingsPage').Visibility = if ($Settings) { 'Visible' } else { 'Collapsed' }
$window.FindName('HeaderVersion').Text = '   VPN / 1.0.1'
$window.FindName('SiteModeBox').SelectedIndex = 1
$window.FindName('GeoModeBox').SelectedIndex = 2
$window.FindName('VpnSitesBox').Visibility = 'Visible'
$window.FindName('DirectSitesBox').Visibility = 'Collapsed'
$window.FindName('VpnSitesBox').Text = "youtube.com`r`ngooglevideo.com"
$window.FindName('DirectSitesBox').Text = 'example.com'
$content = $window.Content
$content.Measure([Windows.Size]::new(1220,830))
$content.Arrange([Windows.Rect]::new(0,0,1220,830))
$content.UpdateLayout()
function Save-Frame($name) {
    $image = [Windows.Media.Imaging.RenderTargetBitmap]::new(1220,830,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $image.Render($content)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($image))
    $path = Join-Path $root "tests/generated/$name.png"
    $stream = [IO.File]::Create($path)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
    Write-Output $path
}
if ($Settings) {
    Save-Frame 'settings-top-1.0.2'
    $window.FindName('SettingsPage').ScrollToEnd()
    $content.UpdateLayout()
    Save-Frame 'settings-bottom-1.0.2'
    $window.Close()
    $app.Shutdown()
    return
}
Save-Frame 'routing-top-1.0.1'
$window.FindName('VpnSitesBox').BringIntoView()
$content.UpdateLayout()
Save-Frame 'routing-sites-1.0.1'
$routing = $window.FindName('RoutingPage')
$routing.Children[1].Child.ScrollToEnd()
$content.UpdateLayout()
Save-Frame 'routing-bottom-1.0.1'
$window.Close()
$app.Shutdown()
