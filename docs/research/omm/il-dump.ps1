# Dumps a .NET assembly's IL, one method at a time, and saves its embedded
# resources (OMM keeps logi_nethidppio.dll and its other DLLs there). Uses only the
# metadata reader that ships with .NET (PowerShell 7); loads and runs nothing
# from the assembly. Methods compiled to native code (most of logi_nethidppio)
# are listed as "(native or abstract)".
#
#   pwsh -NoProfile -File il-dump.ps1 OnboardMemoryManager_2.7.4428.exe out\omm
#   pwsh -NoProfile -File il-dump.ps1 out\omm\res_OnboardMemoryManager.embeddeddlls.logi_nethidppio.dll out\hidppio
param($assembly, $outputDirectory)
New-Item -ItemType Directory -Force $outputDirectory | Out-Null
$opcodes = @{}
foreach ($field in [System.Reflection.Emit.OpCodes].GetFields()) {
  $opcode = $field.GetValue($null)
  $opcodes[[int]$opcode.Value -band 0xFFFF] = $opcode
}
$bytes = [System.IO.File]::ReadAllBytes($assembly)
$peReader = New-Object System.Reflection.PortableExecutable.PEReader(,[System.Collections.Immutable.ImmutableArray]::Create([byte[]]$bytes))
$metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($peReader)
$tokens = [System.Reflection.Metadata.Ecma335.MetadataTokens]

function Get-TypeName($handle) {
  if ($handle.IsNil) { return "" }
  switch ($handle.Kind) {
    'TypeDefinition' { return $metadata.GetString($metadata.GetTypeDefinition([System.Reflection.Metadata.TypeDefinitionHandle]$handle).Name) }
    'TypeReference' { return $metadata.GetString($metadata.GetTypeReference([System.Reflection.Metadata.TypeReferenceHandle]$handle).Name) }
    'TypeSpecification' { return "typespec" }
  }
  return "$($handle.Kind)"
}

function Resolve-Token([int]$token) {
  $handle = $tokens::EntityHandle($token)
  try {
    switch ($handle.Kind) {
      'MethodDefinition' { $method = $metadata.GetMethodDefinition([System.Reflection.Metadata.MethodDefinitionHandle]$handle); return (Get-TypeName $method.GetDeclaringType()) + "::" + $metadata.GetString($method.Name) }
      'MemberReference' { $member = $metadata.GetMemberReference([System.Reflection.Metadata.MemberReferenceHandle]$handle); return (Get-TypeName $member.Parent) + "::" + $metadata.GetString($member.Name) }
      'FieldDefinition' { $field = $metadata.GetFieldDefinition([System.Reflection.Metadata.FieldDefinitionHandle]$handle); return (Get-TypeName $field.GetDeclaringType()) + "::" + $metadata.GetString($field.Name) }
      'MethodSpecification' { $specification = $metadata.GetMethodSpecification([System.Reflection.Metadata.MethodSpecificationHandle]$handle); return (Resolve-Token ($tokens::GetToken($specification.Method))) + "<>" }
      default { return Get-TypeName $handle }
    }
  } catch { return "token" + $token.ToString("x8") }
}

function Get-SignedByte($value) { return ([int]$value -bxor 0x80) - 0x80 }

$output = New-Object System.Text.StringBuilder
foreach ($typeHandle in $metadata.TypeDefinitions) {
  $type = $metadata.GetTypeDefinition($typeHandle)
  $typeName = $metadata.GetString($type.Namespace) + "." + $metadata.GetString($type.Name)
  if (-not $type.GetDeclaringType().IsNil) { $typeName = (Get-TypeName $type.GetDeclaringType()) + "/" + $metadata.GetString($type.Name) }
  foreach ($methodHandle in $type.GetMethods()) {
    $method = $metadata.GetMethodDefinition($methodHandle)
    [void]$output.AppendLine("=== $typeName::" + $metadata.GetString($method.Name))
    if ($method.RelativeVirtualAddress -eq 0 -or ([int]$method.ImplAttributes -band 3) -ne 0) { [void]$output.AppendLine("  (native or abstract)"); continue }
    $il = [System.Reflection.Metadata.PEReaderExtensions]::GetMethodBody($peReader, $method.RelativeVirtualAddress).GetILBytes()
    $position = 0
    while ($position -lt $il.Length) {
      $start = $position
      $code = [int]$il[$position]; $position++
      if ($code -eq 0xFE) { $code = 0xFE00 -bor $il[$position]; $position++ }
      $opcode = $opcodes[$code]
      if ($null -eq $opcode) { [void]$output.AppendLine("  unknown opcode $code"); break }
      $operand = ""
      switch ($opcode.OperandType) {
        'InlineNone' {}
        'ShortInlineBrTarget' { $operand = "L" + ($position + 1 + (Get-SignedByte $il[$position])); $position += 1 }
        'ShortInlineI' { $operand = [string](Get-SignedByte $il[$position]); $position += 1 }
        'ShortInlineVar' { $operand = [string]$il[$position]; $position += 1 }
        'InlineVar' { $operand = [string][BitConverter]::ToUInt16($il, $position); $position += 2 }
        'InlineBrTarget' { $operand = "L" + ($position + 4 + [BitConverter]::ToInt32($il, $position)); $position += 4 }
        'InlineI' { $value = [BitConverter]::ToInt32($il, $position); $operand = "$value (0x" + $value.ToString("x") + ")"; $position += 4 }
        'InlineI8' { $operand = [string][BitConverter]::ToInt64($il, $position); $position += 8 }
        'ShortInlineR' { $operand = [string][BitConverter]::ToSingle($il, $position); $position += 4 }
        'InlineR' { $operand = [string][BitConverter]::ToDouble($il, $position); $position += 8 }
        'InlineString' { $token = [BitConverter]::ToInt32($il, $position); $operand = '"' + $metadata.GetUserString($tokens::UserStringHandle($token -band 0xFFFFFF)) + '"'; $position += 4 }
        'InlineSwitch' {
          $count = [BitConverter]::ToInt32($il, $position); $position += 4
          $targetBase = $position + 4 * $count
          $targets = @()
          for ($index = 0; $index -lt $count; $index++) { $targets += "L" + ($targetBase + [BitConverter]::ToInt32($il, $position)); $position += 4 }
          $operand = $targets -join ","
        }
        default { $operand = Resolve-Token ([BitConverter]::ToInt32($il, $position)); $position += 4 }
      }
      [void]$output.AppendLine("  L$start " + $opcode.Name + " " + $operand)
    }
  }
}
[System.IO.File]::WriteAllText("$outputDirectory\il.txt", $output.ToString())

$resourceList = New-Object System.Collections.Generic.List[string]
$resourceSection = $peReader.GetSectionData($peReader.PEHeaders.CorHeader.ResourcesDirectory.RelativeVirtualAddress)
foreach ($resourceHandle in $metadata.ManifestResources) {
  $resource = $metadata.GetManifestResource($resourceHandle)
  $name = $metadata.GetString($resource.Name)
  $reader = $resourceSection.GetReader([int]$resource.Offset, $resourceSection.Length - [int]$resource.Offset)
  $length = $reader.ReadInt32()
  [System.IO.File]::WriteAllBytes("$outputDirectory\res_$name", $reader.ReadBytes($length))
  $resourceList.Add("$name $length")
}
[System.IO.File]::WriteAllLines("$outputDirectory\resources.txt", $resourceList)
