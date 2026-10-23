param([string[]]$Types)
$m = "$env:USERPROFILE\BSManager\BSInstances\1.40.8\Beat Saber_Data\Managed"
[AppDomain]::CurrentDomain.add_AssemblyResolve({ param($s,$e) $n=($e.Name -split ',')[0]; $p=Join-Path $m "$n.dll"; if(Test-Path $p){[Reflection.Assembly]::LoadFrom($p)} else {$null} })
$asms = @('Main','DataModels','BeatmapCore','GameplayCore','BGLib.UnityExtension','HMLib','HMUI','BeatSaber.ViewSystem','Zenject','BeatSaber.GameSettings') | % { $p=Join-Path $m "$_.dll"; if(Test-Path $p){[Reflection.Assembly]::LoadFrom($p)} }
$bf=[Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly'
foreach($t in $Types){
  $ty=$null; foreach($a in $asms){ try{ $ty=$a.GetType($t) }catch{}; if($ty){break} }
  if(-not $ty){ foreach($a in $asms){ try{ $ty = $a.GetTypes() | ? Name -eq $t | select -First 1 }catch{ $ty = $_.Exception.Types | ? {$_ -and $_.Name -eq $t} | select -First 1 }; if($ty){break} } }
  if(-not $ty){ "### $t NOT FOUND"; continue }
  "### $($ty.FullName) [$($ty.Assembly.GetName().Name)] : $($ty.BaseType)"
  try{ $ty.GetFields($bf) | % { "  F $($_.FieldType.Name) $($_.Name)" } }catch{ "  fields err $_" }
  try{ $ty.GetProperties($bf) | % { "  P $($_.PropertyType.Name) $($_.Name)" } }catch{ "  props err" }
  try{ $ty.GetEvents($bf) | % { "  E $($_.EventHandlerType.Name) $($_.Name)" } }catch{}
  try{ $ty.GetMethods($bf) | ? {-not $_.IsSpecialName} | % { "  M $($_.ReturnType.Name) $($_.Name)(" + (($_.GetParameters()|%{ "$($_.ParameterType.Name) $($_.Name)"}) -join ', ') + ")" } }catch{ "  methods err $_" }
  try{ $ty.GetConstructors($bf) | % { "  C (" + (($_.GetParameters()|%{ "$($_.ParameterType.Name) $($_.Name)"}) -join ', ') + ")" } }catch{}
}
