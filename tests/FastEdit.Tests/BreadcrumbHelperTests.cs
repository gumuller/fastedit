using FastEdit.Helpers;

namespace FastEdit.Tests;

public class BreadcrumbHelperTests
{
    [Fact]
    public void GetBreadcrumbs_CSharp_ReturnsContainingScopes()
    {
        var text = """
            namespace Demo {
                class Sample {
                    void Run() {
                        Console.WriteLine("hi");
                    }
                }
            }
            """;

        var breadcrumbs = BreadcrumbHelper.GetBreadcrumbs(text, caretLine: 4, language: "C#");

        Assert.Equal(
            [
                new BreadcrumbHelper.BreadcrumbItem("Demo", "namespace", 1),
                new BreadcrumbHelper.BreadcrumbItem("Sample", "class", 2),
                new BreadcrumbHelper.BreadcrumbItem("Run", "method", 3)
            ],
            breadcrumbs);
    }

    [Fact]
    public void GetBreadcrumbs_CSharp_IgnoresBracesInsideStringsAndComments()
    {
        var text = """
            class Sample {
                string text = "{";
                // ignored {
                void Run() {
                    Console.WriteLine(text);
                }
            }
            """;

        var breadcrumbs = BreadcrumbHelper.GetBreadcrumbs(text, caretLine: 5, language: "C#");

        Assert.Equal(
            [
                new BreadcrumbHelper.BreadcrumbItem("Sample", "class", 1),
                new BreadcrumbHelper.BreadcrumbItem("Run", "method", 4)
            ],
            breadcrumbs);
    }

    [Fact]
    public void GetBreadcrumbs_Python_ReturnsIndentScopes()
    {
        var text = """
            class Sample:
                async def run(self):
                    print("hi")
            """;

        var breadcrumbs = BreadcrumbHelper.GetBreadcrumbs(text, caretLine: 3, language: "Python");

        Assert.Equal(
            [
                new BreadcrumbHelper.BreadcrumbItem("Sample", "class", 1),
                new BreadcrumbHelper.BreadcrumbItem("run", "def", 2)
            ],
            breadcrumbs);
    }
}
