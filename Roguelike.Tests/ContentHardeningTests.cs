using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;
using Roguelike.Content;
using Xunit;

namespace Roguelike.Tests;

public sealed class ContentHardeningTests
{
    private static string CreateTempContent(Action<JsonObject, JsonObject, JsonObject> mutate)
    {
        // Find default content directory to copy from
        string source = FindContentDirectory();
        string target = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(target);
        
        foreach (string name in new[] { "monsters.json", "items.json", "balance.json" })
            File.Copy(Path.Combine(source, name), Path.Combine(target, name));

        JsonObject monsters = JsonNode.Parse(File.ReadAllText(Path.Combine(target, "monsters.json")))!.AsObject();
        JsonObject items = JsonNode.Parse(File.ReadAllText(Path.Combine(target, "items.json")))!.AsObject();
        JsonObject balance = JsonNode.Parse(File.ReadAllText(Path.Combine(target, "balance.json")))!.AsObject();
        
        mutate(monsters, items, balance);
        
        File.WriteAllText(Path.Combine(target, "monsters.json"), monsters.ToJsonString());
        File.WriteAllText(Path.Combine(target, "items.json"), items.ToJsonString());
        File.WriteAllText(Path.Combine(target, "balance.json"), balance.ToJsonString());
        
        return target;
    }

    private static string FindContentDirectory()
    {
        string baseDir = AppContext.BaseDirectory;
        while (baseDir != null)
        {
            string candidate = Path.Combine(baseDir, "Content");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "balance.json"))) 
                return candidate;
            baseDir = Directory.GetParent(baseDir)?.FullName;
        }
        throw new DirectoryNotFoundException("Default Content directory not found for tests.");
    }

    [Fact]
    public void AggregatesMultipleErrorsAcrossFiles()
    {
        string dir = CreateTempContent((monsters, items, balance) =>
        {
            // 1. Bad glyph in monsters
            var m = monsters["monsters"]!.AsArray().First().AsObject();
            m["glyph"] = "TooLong";

            // 2. Bad value in balance
            balance["stairHealPercent"] = 150;

            // 3. Unknown effect type in items
            var i = items["items"]!.AsArray().First().AsObject();
            var effects = i["effects"]!.AsArray();
            effects[0].AsObject()["type"] = "magic_beam";
        });

        try
        {
            var ex = Assert.Throws<ContentLoadException>(() => ContentDatabase.LoadDirectory(dir));
            Assert.Contains("glyph must be exactly one printable ASCII char", ex.Message);
            Assert.Contains("stairHealPercent must be between 0 and 100", ex.Message);
            Assert.Contains("effects type 'magic_beam' is unknown", ex.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ReportsCorrectArrayIndicesForSequentialErrors()
    {
        string dir = CreateTempContent((monsters, _, _) =>
        {
            var array = monsters["monsters"]!.AsArray();
            array[0].AsObject()["id"] = "";
            array[1].AsObject()["id"] = "";
        });

        try
        {
            var ex = Assert.Throws<ContentLoadException>(() => ContentDatabase.LoadDirectory(dir));
            Assert.Contains("monsters[0].id must not be empty", ex.Message);
            Assert.Contains("monsters[1].id must not be empty", ex.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void RejectsMissingRequiredFields()
    {
        string dir = CreateTempContent((monsters, _, _) =>
        {
            var m = monsters["monsters"]!.AsArray().First().AsObject();
            m.Remove("name");
        });

        try
        {
            var ex = Assert.Throws<ContentLoadException>(() => ContentDatabase.LoadDirectory(dir));
            Assert.Contains("name must not be empty", ex.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void RejectsInvalidTypes()
    {
        string dir = CreateTempContent((monsters, _, _) =>
        {
            var m = monsters["monsters"]!.AsArray().First().AsObject();
            m["maxHp"] = "a lot"; // String instead of int
        });

        try
        {
            var ex = Assert.Throws<ContentLoadException>(() => ContentDatabase.LoadDirectory(dir));
            Assert.Contains("JSON error", ex.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void RejectsMalformedJson()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "monsters.json"), "{ \"monsters\": [ { \"id\": \"rat\" "); // Missing closing brackets
        File.WriteAllText(Path.Combine(dir, "items.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "balance.json"), "{}");

        try
        {
            var ex = Assert.Throws<ContentLoadException>(() => ContentDatabase.LoadDirectory(dir));
            Assert.Contains("JSON error", ex.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void RejectsMonsterWithMissingBehaviorParams()
    {
        string dir = CreateTempContent((monsters, _, _) =>
        {
            var m = monsters["monsters"]!.AsArray().First(n => n!["id"]!.GetValue<string>() == "archer")!.AsObject();
            m["behavior"] = "ranged";
            m["params"]!.AsObject().Remove("maxRange");
        });

        try
        {
            var ex = Assert.Throws<ContentLoadException>(() => ContentDatabase.LoadDirectory(dir));
            Assert.Contains("maxRange is required", ex.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void RejectsBalanceWithMissingField()
    {
        string dir = CreateTempContent((_, _, balance) =>
        {
            balance.Remove("startingHp");
        });

        try
        {
            Assert.Throws<ContentLoadException>(() => ContentDatabase.LoadDirectory(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void RejectsEmptyDepthCoverage()
    {
        string dir = CreateTempContent((monsters, items, _) =>
        {
            foreach (var m in monsters["monsters"]!.AsArray())
                m.AsObject()["minDepth"] = 11;
        });

        try
        {
            var ex = Assert.Throws<ContentLoadException>(() => ContentDatabase.LoadDirectory(dir));
            Assert.Contains("depth 1 must have at least one monster", ex.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FuzzTest_NoRawExceptions()
    {
        Random rng = new(42);
        for (int i = 0; i < 200; i++)
        {
            string dir = CreateTempContent((monsters, items, balance) =>
            {
                // Random mutation
                int choice = rng.Next(3);
                if (choice == 0) // Corrupt monster
                {
                    var array = monsters["monsters"]!.AsArray();
                    var item = array[rng.Next(array.Count)].AsObject();
                    if (rng.Next(2) == 0) item.Remove("id");
                    else item["maxHp"] = "garbage";
                }
                else if (choice == 1) // Corrupt item
                {
                    var array = items["items"]!.AsArray();
                    var item = array[rng.Next(array.Count)].AsObject();
                    if (rng.Next(2) == 0) item.Remove("glyph");
                    else item["weight"] = -1;
                }
                else // Corrupt balance
                {
                    balance.Remove("startingHp");
                }
            });

            try
            {
                // We don't care if it succeeds or fails, just that it doesn't throw a raw exception
                ContentDatabase.LoadDirectory(dir);
            }
            catch (ContentLoadException) { }
            catch (Exception ex)
            {
                Assert.Fail($"Fuzz iteration {i} threw raw exception: {ex.GetType().Name}: {ex.Message}");
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
