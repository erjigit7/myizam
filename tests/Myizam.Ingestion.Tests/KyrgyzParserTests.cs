using Myizam.Ingestion.Parsers;
using Xunit;

namespace Myizam.Ingestion.Tests;

/// <summary>Кыргызский парсер: фикстуры реального HTML (tests/fixtures/kg) + маркеры старых кодексов.</summary>
public class KyrgyzParserTests
{
    private static readonly MinjustHtmlParser Parser = new();

    private static ParsedLaw ParseKgFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "kg", name);
        return Parser.Parse(File.ReadAllText(path), lang: "kg");
    }

    private static ParsedLaw ParseKg(string html) => Parser.Parse(html, lang: "kg");

    [Fact]
    public void Real_TK_fragment_new_style_markers()
    {
        // «I БӨЛҮМ. ЖАЛПЫ ЖОБОЛОР» + «1-глава.» + «1-берене.» — реальный ТК-2025
        var law = ParseKgFixture("section_chapter_article.html");

        Assert.Single(law.SectionHeadings);
        Assert.Contains("БӨЛҮМ", law.SectionHeadings[0]);
        Assert.NotEmpty(law.Articles);
        Assert.Equal("1", law.Articles[0].Number);
        Assert.Contains("түшүнүктөр", law.Articles[0].Title);
    }

    [Fact]
    public void Real_TK_article_38_with_intext_reference()
    {
        var law = ParseKgFixture("article_38_with_reference.html");

        var a38 = law.Articles.FirstOrDefault(a => a.Number == "38");
        Assert.NotNull(a38);
        // внутритекстовая ссылка «40-беренесинин» (родительный падеж) не рвёт статью
        Assert.Contains("40-беренесинин", a38!.Text);
    }

    [Fact]
    public void Old_style_N_statya_marker()
    {
        // СК-2003: «11-статья.» — русское слово после номера
        var law = ParseKg("""
            <html><body>
            <p>3-Глава Никеге туруунун шарттары жана тартиби
            <p>11-статья. Никеге туруу
            <p>1. Никеге туруу жарандык абалдын актыларын жазуу органдарында жүргүзүлөт.
            <p>12-статья. Никеге туруунун тартиби
            <p>Текст экинчи статьянын.
            </body></html>
            """);

        Assert.Equal(new[] { "11", "12" }, law.Articles.Select(a => a.Number).ToArray());
        Assert.Equal("Никеге туруу", law.Articles[0].Title);
        Assert.Single(law.ChapterHeadings);   // «3-Глава …» без точки — глава
    }

    [Fact]
    public void Case_form_glavada_is_text_not_chapter()
    {
        // «34-1-главада каралбаган…» — падежная форма в начале абзаца = ТЕКСТ статьи
        var law = ParseKg("""
            <html><body>
            <p>738-58-статья. Жалпы жоболор
            <p>34-1-главада каралбаган бүтүмдөр мыйзамдар менен жөнгө салынат.
            </body></html>
            """);

        var a = Assert.Single(law.Articles);
        Assert.Contains("34-1-главада", a.Text);
        Assert.Empty(law.ChapterHeadings);
    }

    [Fact]
    public void Typo_hard_sign_statya_and_superscript_space()
    {
        // «статЪя» (Ъ, ГК-II ст.738-36) + пробел от суперскрипта в номере
        var law = ParseKg("""
            <html><body>
            <p>738<sup>36</sup>-статъя. Истиснаа келишими
            <p>Истиснаа келишими боюнча бир тарап экинчи тарапка буюм жасайт.
            </body></html>
            """);

        var a = Assert.Single(law.Articles);
        Assert.Equal("738-36", a.Number);
        Assert.Equal("Истиснаа келишими", a.Title);
    }

    [Fact]
    public void Merged_header_and_body_goes_to_body()
    {
        // слитый заголовок+тело в одном абзаце → текст не теряется, статья не «пустая»
        var longBody = string.Join(" ", Enumerable.Repeat("Мыйзам менен жөнгө салынуучу мамилелер боюнча жоболор колдонулат.", 6));
        var law = ParseKg($"""
            <html><body>
            <p>5-берене. {longBody}
            </body></html>
            """);

        var a = Assert.Single(law.Articles);
        Assert.Null(a.Title);
        Assert.Contains("жөнгө салынуучу", a.Text);
    }
}
