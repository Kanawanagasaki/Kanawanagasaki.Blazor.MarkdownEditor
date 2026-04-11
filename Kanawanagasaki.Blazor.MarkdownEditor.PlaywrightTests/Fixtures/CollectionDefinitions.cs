using Xunit;

namespace Kanawanagasaki.Blazor.MarkdownEditor.Tests.Fixtures;

/// <summary>
/// xunit collection definition so that all editor tests share the same
/// TestAppFixture instance (single Blazor server + single Playwright browser).
/// </summary>
[CollectionDefinition(EditorTestCollection.Name)]
public class EditorTestCollection : ICollectionFixture<TestAppFixture>
{
    public const string Name = "EditorTests";
}
