// Пример корректного использования генерации YAML
// Файл: ReportAndValidationService.cs

// ✅ ПРАВИЛЬНЫЙ способ генерации пустых коллекций:

if (analysis.DetectedFrameworks.Any())
{
    sb.AppendLine("  detectedFrameworks:");
    foreach (var fw in analysis.DetectedFrameworks)
        sb.AppendLine($"    - '{EscapeYamlString(fw)}'");
}
else
{
    sb.AppendLine("  detectedFrameworks: []");  // ✅ В одну строку!
}

// ✅ ПРАВИЛЬНЫЙ способ для объектов:

if (analysis.DetectedPorts.Any())
{
    sb.AppendLine("  detectedPorts:");
    foreach (var port in analysis.DetectedPorts)
        sb.AppendLine($"    {port.Key}: '{EscapeYamlString(port.Value)}'");
}
else
{
    sb.AppendLine("  detectedPorts: {}");  // ✅ В одну строку!
}

/* 
❌ НЕПРАВИЛЬНО:
sb.AppendLine("  detectedFrameworks:");
sb.AppendLine("    []");  // Это создаст ошибку YAML!

❌ НЕПРАВИЛЬНО:
sb.AppendLine("  detectedPorts:");
sb.AppendLine("    {}");  // Это создаст ошибку YAML!
*/

