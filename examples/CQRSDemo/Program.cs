using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// CQRS Demo - Complex Type ES Test Client
/// 
/// Usage:
///   dotnet run -- --api https://localhost:44345
///   dotnet run -- --api https://localhost:44345 --verbose
/// 
/// Requires: HttpApi.Host running (Local mode for full test)
/// Tests: ES handling of List, Dictionary, nested objects
/// </summary>

var apiUrl = GetArg(args, "--api") ?? "https://localhost:44345";
var verbose = args.Contains("--verbose");

Console.WriteLine("╔══════════════════════════════════════════════════════╗");
Console.WriteLine("║   CQRS Demo - Complex Type ES Test                   ║");
Console.WriteLine("║   Testing: List, Dictionary, Nested Objects          ║");
Console.WriteLine("╚══════════════════════════════════════════════════════╝");
Console.WriteLine($"\n📡 API: {apiUrl}");
Console.WriteLine();

// Allow self-signed certificates for local HTTPS testing
var handler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
};
using var client = new HttpClient(handler) { BaseAddress = new Uri(apiUrl) };

try
{
    // Test 1: Health Check
    Console.WriteLine("1️⃣  Health Check...");
    var health = await client.GetAsync("/api/agent-demo/health");
    if (health.IsSuccessStatusCode)
    {
        Console.WriteLine($"   ✅ Status: {health.StatusCode}");
    }
    else
    {
        Console.WriteLine($"   ❌ Status: {health.StatusCode}");
        Console.WriteLine("   Make sure HttpApi.Host is running!");
        return;
    }

    // Test 2: Create Complex State Agent with test data
    Console.WriteLine("\n2️⃣  Creating ComplexStateAgent with test data...");
    var createResponse = await client.PostAsync("/api/agent-demo/complex-agent", null);
    
    if (!createResponse.IsSuccessStatusCode)
    {
        var error = await createResponse.Content.ReadAsStringAsync();
        Console.WriteLine($"   ❌ Failed: {createResponse.StatusCode}");
        if (verbose) Console.WriteLine($"   {error}");
        return;
    }
    
    var createResult = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
    var agentId = createResult.GetProperty("agentId").GetString();
    var agentType = createResult.GetProperty("agentType").GetString();
    var testDataInit = createResult.GetProperty("testDataInitialized").GetBoolean();
    
    Console.WriteLine($"   ✅ Agent ID: {agentId}");
    Console.WriteLine($"   ✅ Agent Type: {agentType}");
    Console.WriteLine($"   ✅ Test Data Initialized: {testDataInit}");

    // Test 3: Get Agent State directly (verify complex data)
    Console.WriteLine("\n3️⃣  Verifying Agent State (from Agent)...");
    var stateResponse = await client.GetAsync($"/api/agent-demo/complex-agent/{agentId}/state");
    
    if (stateResponse.IsSuccessStatusCode)
    {
        var state = await stateResponse.Content.ReadAsStringAsync();
        Console.WriteLine("   ✅ Agent State:");
        try
        {
            var json = JsonDocument.Parse(state);
            var formatted = JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true });
            foreach (var line in formatted.Split('\n').Take(30))
            {
                Console.WriteLine($"      {line}");
            }
            if (formatted.Split('\n').Length > 30)
            {
                Console.WriteLine($"      ... (truncated)");
            }
        }
        catch
        {
            Console.WriteLine($"      {state}");
        }
    }
    else
    {
        Console.WriteLine($"   ⚠️ Could not get agent state: {stateResponse.StatusCode}");
    }

    // Wait for state projection to ES
    Console.WriteLine("\n⏳ Waiting for state projection to ES (3s)...");
    await Task.Delay(3000);

    // Test 4: Query State from Elasticsearch
    Console.WriteLine("\n4️⃣  Query State from Elasticsearch...");
    Console.WriteLine($"   Agent Type: {agentType}");
    Console.WriteLine($"   Agent ID: {agentId}");
    
    var esResponse = await client.GetAsync($"/api/states/{agentType}/{agentId}");
    
    if (esResponse.IsSuccessStatusCode)
    {
        var esState = await esResponse.Content.ReadAsStringAsync();
        Console.WriteLine("   ✅ ES State retrieved:");
        
        try
        {
            var json = JsonDocument.Parse(esState);
            var formatted = JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true });
            
            // Print with analysis
            Console.WriteLine("\n   📊 ES Data Analysis:");
            var root = json.RootElement;
            
            if (root.TryGetProperty("data", out var data))
            {
                Console.WriteLine("\n   ══════ Basic Types ══════");
                PrintProperty(data, "name", "string");
                PrintProperty(data, "age", "int");
                PrintProperty(data, "balance", "double");
                PrintProperty(data, "isActive", "bool");
                
                Console.WriteLine("\n   ══════ Nested Object (address) ══════");
                PrintProperty(data, "address", "nested object → JSON string");
                
                Console.WriteLine("\n   ══════ List<string> (tags) ══════");
                PrintProperty(data, "tags", "repeated string → JSON array string");
                
                Console.WriteLine("\n   ══════ List<nested> (orders) ══════");
                PrintProperty(data, "orders", "repeated OrderItem → JSON array string");
                
                Console.WriteLine("\n   ══════ Map<string,string> (metadata) ══════");
                PrintProperty(data, "metadata", "map → JSON object string");
                
                Console.WriteLine("\n   ══════ Map<string,int> (scores) ══════");
                PrintProperty(data, "scores", "map → JSON object string");
                
                Console.WriteLine("\n   ══════ List<int> (luckyNumbers) ══════");
                PrintProperty(data, "luckyNumbers", "repeated int → JSON array string");
            }
            
            Console.WriteLine("\n   ══════ Full ES Document ══════");
            foreach (var line in formatted.Split('\n'))
            {
                Console.WriteLine($"      {line}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"      Raw: {esState}");
            if (verbose) Console.WriteLine($"      Parse error: {ex.Message}");
        }
    }
    else
    {
        Console.WriteLine($"   ⚠️ State not in ES: {esResponse.StatusCode}");
        if (verbose)
        {
            var body = await esResponse.Content.ReadAsStringAsync();
            Console.WriteLine($"      {body}");
        }
        Console.WriteLine("\n   💡 Troubleshooting:");
        Console.WriteLine("      1. Check if IStateProjector is configured in HttpApi.Host");
        Console.WriteLine("      2. Verify Elasticsearch is running (http://localhost:9200)");
        Console.WriteLine("      3. Check logs for projection errors");
    }

    // Test 5: Count Query
    Console.WriteLine("\n5️⃣  Count ComplexStateAgent in ES...");
    var countResponse = await client.GetAsync($"/api/states/{agentType}/count");
    if (countResponse.IsSuccessStatusCode)
    {
        var countResult = await countResponse.Content.ReadFromJsonAsync<JsonElement>();
        Console.WriteLine($"   ✅ Total documents: {countResult.GetProperty("count").GetInt64()}");
    }
    else
    {
        Console.WriteLine($"   ⚠️ Count failed: {countResponse.StatusCode}");
    }

    // Test 6: Search with query (using POST /api/states/query)
    Console.WriteLine("\n6️⃣  Search with query string...");
    var queryRequest = new
    {
        agentType = agentType,
        queryString = "name:Alice*",
        pageIndex = 0,
        pageSize = 10
    };
    var searchResponse = await client.PostAsJsonAsync("/api/states/query", queryRequest);
    if (searchResponse.IsSuccessStatusCode)
    {
        var searchResult = await searchResponse.Content.ReadFromJsonAsync<JsonElement>();
        Console.WriteLine($"   ✅ Found: {searchResult.GetProperty("totalCount").GetInt64()} results");
        if (searchResult.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
        {
            Console.WriteLine($"   ✅ Retrieved {items.GetArrayLength()} items in this page");
        }
    }
    else
    {
        Console.WriteLine($"   ⚠️ Search failed: {searchResponse.StatusCode}");
        if (verbose)
        {
            var body = await searchResponse.Content.ReadAsStringAsync();
            Console.WriteLine($"      {body}");
        }
    }

    Console.WriteLine("\n════════════════════════════════════════════════════════════");
    Console.WriteLine("✅ Complex Type CQRS Test Complete!");
    Console.WriteLine("════════════════════════════════════════════════════════════");
    Console.WriteLine("\n📝 Summary:");
    Console.WriteLine("   • Basic types (string, int, double, bool): Stored as native ES types");
    Console.WriteLine("   • Nested objects: Serialized to JSON string");
    Console.WriteLine("   • List<T>: Serialized to JSON array string");
    Console.WriteLine("   • Map<K,V>: Serialized to JSON object string");
    Console.WriteLine("\n");
}
catch (HttpRequestException ex)
{
    Console.WriteLine($"\n❌ Connection failed: {ex.Message}");
    Console.WriteLine($"   Make sure HttpApi.Host is running at {apiUrl}");
    Console.WriteLine("\n📖 To start HttpApi.Host (Local mode):");
    Console.WriteLine("   cd apps/Aevatar.App/src/Aevatar.App.HttpApi.Host");
    Console.WriteLine("   dotnet run");
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ Error: {ex.Message}");
    if (verbose) Console.WriteLine(ex.StackTrace);
}

static string? GetArg(string[] args, string key)
{
    var idx = Array.IndexOf(args, key);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}

static void PrintProperty(JsonElement data, string propName, string expectedType)
{
    if (data.TryGetProperty(propName, out var prop))
    {
        var value = prop.ToString();
        if (value.Length > 60) value = value.Substring(0, 60) + "...";
        Console.WriteLine($"      {propName}: {value}");
        Console.WriteLine($"      └─ Type: {prop.ValueKind} (Expected: {expectedType})");
    }
    else
    {
        Console.WriteLine($"      {propName}: <not found>");
    }
}
