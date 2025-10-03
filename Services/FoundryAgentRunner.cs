using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.AI.Agents.Persistent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using app_tramites.Models.ModelAi;
using Azure.AI.Projects;
using System.Reflection;

public static class FoundryAgentRunner
{
    private static async Task RunAgentConversation()
    {
        // --- Valores que ya compartiste ---
        string rawConnectionString = "eastus2.api.azureml.ms;406ae2da-872d-4196-a871-e36dfeb4549f;operaciones-ai;convenio-ai-py";
        string agentId = "asst_qxKdsilOHFNvArsJ4Yd61PRW";

        // --- Parse simple de la connection string (ASUNCIÓN) ---
        var parts = rawConnectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) throw new InvalidOperationException("connectionString no tiene el formato esperado: host;id;project;...");
        string host = parts[0];
        string projectName = parts[2];
        string endpoint = $"https://{host}/api/projects/{projectName}";

        Console.WriteLine($"Endpoint: {endpoint}");
        Console.WriteLine($"AgentId: {agentId}");

        var credential = new DefaultAzureCredential();
        var projectClient = new AIProjectClient(new Uri(endpoint), credential);
        PersistentAgentsClient agentsClient = projectClient.GetPersistentAgentsClient();

        // Crear hilo y enviar mensaje
        var threadResp = await agentsClient.Threads.CreateThreadAsync();
        var thread = threadResp.Value;
        Console.WriteLine($"Thread creado: {thread.Id}");

        var userText = "Hola desde el cliente. Probar conversation y archivos.";
        var msgResp = await agentsClient.Messages.CreateMessageAsync(thread.Id, MessageRole.User, userText);
        Console.WriteLine($"Mensaje enviado: {msgResp.Value.Id}");

        // Crear y ejecutar Run
        var runResp = await agentsClient.Runs.CreateRunAsync(thread.Id, agentId);
        var run = runResp.Value;
        Console.WriteLine($"Run creado: {run.Id} - status: {run.Status}");

        // Polling hasta estado terminal
        while (true)
        {
            await Task.Delay(500);
            run = (await agentsClient.Runs.GetRunAsync(thread.Id, run.Id)).Value;
            Console.WriteLine($"Run status: {run.Status}");
            if (run.Status != RunStatus.Queued && run.Status != RunStatus.InProgress) break;
        }

        Console.WriteLine("---- Mensajes del hilo ----");

        // Iterar mensajes: AsyncPageable<T> -> await foreach
        var asyncMessages = agentsClient.Messages.GetMessagesAsync(thread.Id);
        await foreach (var msg in asyncMessages)
        {
            await HandlePersistentMessageAsync(msg);
        }

        Console.WriteLine("Fin.");
    }

    private static async Task HandlePersistentMessageAsync(PersistentThreadMessage msg)
    {
        // Imprimir cabecera
        Console.Write($"{msg.CreatedAt:yyyy-MM-dd HH:mm:ss} - {msg.Role,10}: ");

        bool printed = false;

        // 1) Si hay MessageTextContent(s), mostrarlos
        if (msg.ContentItems is not null && msg.ContentItems.Count > 0)
        {
            foreach (var item in msg.ContentItems)
            {
                if (item is MessageTextContent textItem)
                {
                    Console.Write(textItem.Text);
                    printed = true;
                }
            }
        }

        if (!printed) Console.Write("[sin texto]");

        Console.WriteLine();

        // 2) Para todos los content items: inspeccionar y, si tienen URL, intentar descargarlos
        if (msg.ContentItems is not null && msg.ContentItems.Count > 0)
        {
            int index = 0;
            foreach (var item in msg.ContentItems)
            {
                index++;
                Console.WriteLine($"  - ContentItem tipo: {item.GetType().FullName}");

                // Mostrar propiedades públicas y sus valores (limitar tamaño a 300 chars)
                var props = item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
                foreach (var p in props.OrderBy(p => p.Name))
                {
                    object val = null;
                    try { val = p.GetValue(item); } catch { val = "[no-access]"; }
                    if (val != null)
                    {
                        var s = val.ToString();
                        if (s.Length > 300) s = s.Substring(0, 300) + "...";
                        Console.WriteLine($"      {p.Name} = {s}");
                    }
                }

                // Intento práctico: encontrar la primera propiedad string que parezca una URL
                string[] candidatePropNames = new[] { "Url", "Uri", "FileUrl", "SasUrl", "DownloadUrl", "FileUri" };
                string foundUrl = null;
                string foundFileName = null;

                // 1) buscar por nombres comunes
                foreach (var name in candidatePropNames)
                {
                    var pi = item.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (pi != null)
                    {
                        try
                        {
                            var v = pi.GetValue(item)?.ToString();
                            if (!string.IsNullOrEmpty(v) && Uri.IsWellFormedUriString(v, UriKind.Absolute))
                            {
                                foundUrl = v;
                                break;
                            }
                        }
                        catch { /* ignore */ }
                    }
                }

                // 2) si no encontramos, buscar cualquier propiedad string que tenga http(s)
                if (foundUrl == null)
                {
                    foreach (var p in props)
                    {
                        if (p.PropertyType == typeof(string))
                        {
                            try
                            {
                                var v = p.GetValue(item) as string;
                                if (!string.IsNullOrEmpty(v) && (v.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || v.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                                {
                                    foundUrl = v;
                                    break;
                                }
                            }
                            catch { /* ignore */ }
                        }
                    }
                }

                // 3) intentar determinar nombre de archivo: propiedades FileName/Name/File or extraer del URL
                string[] filenameCandidates = new[] { "FileName", "Name", "File", "DisplayName" };
                foreach (var fn in filenameCandidates)
                {
                    var pi = item.GetType().GetProperty(fn, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (pi != null)
                    {
                        try
                        {
                            var v = pi.GetValue(item)?.ToString();
                            if (!string.IsNullOrEmpty(v))
                            {
                                foundFileName = v;
                                break;
                            }
                        }
                        catch { }
                    }
                }

                // Si hay URL, intentar descargar
                if (!string.IsNullOrEmpty(foundUrl))
                {
                    if (string.IsNullOrEmpty(foundFileName))
                    {
                        try
                        {
                            var uri = new Uri(foundUrl);
                            var nameFromUrl = Path.GetFileName(uri.LocalPath);
                            foundFileName = string.IsNullOrEmpty(nameFromUrl) ? $"{msg.Id ?? "msg"}_{index}" : nameFromUrl;
                        }
                        catch
                        {
                            foundFileName = $"{msg.Id ?? "msg"}_{index}";
                        }
                    }

                    Console.WriteLine($"      -> Se detectó URL: {foundUrl}");
                    Console.WriteLine($"      -> Nombre elegido: {foundFileName}");

                    try
                    {
                        var saved = await DownloadFileAsync(foundUrl, foundFileName);
                        Console.WriteLine($"      -> Archivo guardado en: {saved}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"      -> Error descargando: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("      -> No se detectó URL en este content item.");
                }
            }
        }
    }

    private static readonly HttpClient _httpClient = new HttpClient();

    private static async Task<string> DownloadFileAsync(string url, string preferredFileName)
    {
        // Carpeta de descargas local
        var downloadsDir = Path.Combine(Directory.GetCurrentDirectory(), "downloads");
        Directory.CreateDirectory(downloadsDir);

        // Sanitizar nombre
        foreach (var c in Path.GetInvalidFileNameChars()) preferredFileName = preferredFileName.Replace(c, '_');

        // Si no tiene extensión, intentar adivinar desde URL
        var ext = Path.GetExtension(preferredFileName);
        string filename = preferredFileName;
        if (string.IsNullOrEmpty(ext))
        {
            try
            {
                var uri = new Uri(url);
                var extFromUrl = Path.GetExtension(uri.LocalPath);
                if (!string.IsNullOrEmpty(extFromUrl))
                    filename = preferredFileName + extFromUrl;
            }
            catch { }
        }

        var savePath = Path.Combine(downloadsDir, filename);

        // Petición simple GET
        using var resp = await _httpClient.GetAsync(url);
        resp.EnsureSuccessStatusCode();

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        await File.WriteAllBytesAsync(savePath, bytes);

        return savePath;
    }

}
