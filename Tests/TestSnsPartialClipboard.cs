using System.Collections.Generic;
using Integra7AuralAlchemist.ViewModels;

namespace Tests;

public class SnsPartialClipboardTests
{
    private sealed class FakeParam : IParam
    {
        public FakeParam(string path, string value) { Path = path; _value = value; }
        private string _value;
        public string Path { get; }
        public string Snapshot() => _value;
        public void ApplyDisplay(string display) => _value = display;
    }

    [Test]
    public void Snapshot_captures_path_to_value()
    {
        var ps = new IParam[] { new FakeParam("a", "1"), new FakeParam("b", "Saw") };
        var snap = SnsPartialClipboard.Snapshot(ps);
        Assert.That(snap["a"], Is.EqualTo("1"));
        Assert.That(snap["b"], Is.EqualTo("Saw"));
    }

    [Test]
    public void Apply_writes_matching_paths_only()
    {
        var a = new FakeParam("a", "1");
        var b = new FakeParam("b", "Saw");
        SnsPartialClipboard.Apply(new IParam[] { a, b },
            new Dictionary<string, string> { ["a"] = "9", ["zzz"] = "ignored" });
        Assert.That(a.Snapshot(), Is.EqualTo("9"));
        Assert.That(b.Snapshot(), Is.EqualTo("Saw"));
    }
}
