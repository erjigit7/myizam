using Myizam.Ingestion;
using Myizam.Ingestion.Parsers;
using Xunit;

namespace Myizam.Ingestion.Tests;

public class MinjustHtmlParserTests
{
    private static readonly MinjustHtmlParser Parser = new();

    private static ParsedLaw ParseFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", name);
        return Parser.Parse(File.ReadAllText(path));
    }

    [Fact]
    public void Article_with_title_on_same_line()
    {
        var law = ParseFixture("article_title_same_line.html");

        Assert.Equal(2, law.Articles.Count);
        var a1 = law.Articles[0];
        Assert.Equal("1", a1.Number);
        Assert.Equal("Основные понятия, используемые в настоящем Кодексе", a1.Title);
        Assert.Equal("РАЗДЕЛ I. ОБЩИЕ ПОЛОЖЕНИЯ", a1.SectionHeading);
        Assert.Equal("Глава 1. Основные положения", a1.ChapterHeading);
        Assert.Contains("Работник - физическое лицо", a1.Text);
        Assert.Equal(2, law.AnchorArticleCount);
    }

    [Fact]
    public void Article_with_title_in_next_paragraph()
    {
        var law = ParseFixture("article_title_next_paragraph.html");

        Assert.Equal(2, law.Articles.Count);
        Assert.Equal("Трудовые отношения и основания их возникновения", law.Articles[0].Title);
        Assert.Contains("основанные на соглашении", law.Articles[0].Text);
        // абзац с точкой на конце — не заголовок, а тело статьи
        Assert.Null(law.Articles[1].Title);
        Assert.Contains("кончается точкой", law.Articles[1].Text);
    }

    [Fact]
    public void Hyphenated_article_numbers_82_1_and_82_1_1()
    {
        var law = ParseFixture("article_hyphen_numbers.html");

        Assert.Equal(new[] { "82", "82-1", "82-1-1", "83" },
            law.Articles.Select(a => a.Number).ToArray());
        Assert.Equal(new[] { 82, 1, 1 }, law.Articles[2].NumberParts());
    }

    [Fact]
    public void Intext_mention_of_article_is_not_a_new_article()
    {
        var law = ParseFixture("mention_not_article.html");

        var a = Assert.Single(law.Articles);
        Assert.Equal("20", a.Number);
        Assert.Contains("со статьей 15", a.Text);
        // «Статья 21 настоящего Кодекса…» в начале абзаца — не заголовок:
        // после номера нет точки, значит это текст статьи 20
        Assert.Contains("Статья 21 настоящего Кодекса", a.Text);
    }

    [Fact]
    public void Table_inside_article_becomes_pipe_separated_rows()
    {
        var law = ParseFixture("table_in_article.html");

        var a = Assert.Single(law.Articles);
        Assert.Contains("Разряд | Коэффициент", a.Text);
        Assert.Contains("1 | 1,00", a.Text);
        Assert.Contains("2 | 1,30", a.Text);
        // текст после таблицы тоже на месте
        Assert.Contains("с учетом квалификации", a.Text);
    }

    [Fact]
    public void Text_before_first_article_goes_to_preamble()
    {
        var law = ParseFixture("preamble.html");

        Assert.NotNull(law.Preamble);
        Assert.Contains("ТРУДОВОЙ КОДЕКС", law.Preamble);
        Assert.Contains("регулирует трудовые отношения", law.Preamble);
        Assert.Contains("вступают в силу с 1 января 2027 года", law.Preamble);
        var a = Assert.Single(law.Articles);
        Assert.Equal("1", a.Number);
    }

    [Fact]
    public void Nbsp_and_soft_hyphens_are_normalized()
    {
        var law = ParseFixture("nbsp_softhyphen.html");

        var a = Assert.Single(law.Articles);
        Assert.Equal("30", a.Number);                       // «Статья&nbsp;30» распознана
        Assert.Equal("Нормализациятекста", a.Title);        // &shy; удалён
        Assert.Contains("Работодатель обязан соблюдать требования.", a.Text);
    }

    [Fact]
    public void Editorial_note_after_title_stays_in_article_text()
    {
        var law = ParseFixture("editorial_note.html");

        var a = Assert.Single(law.Articles);
        Assert.Contains("(В редакции Закона КР от 23 июня 2026 года № 112)", a.Text);
        Assert.Contains("не реже одного раза в месяц", a.Text);
    }

    [Fact]
    public void Superscript_digits_become_hyphenated_insert_numbers()
    {
        var law = ParseFixture("superscript_article.html");

        // «Статья 21<sup>1</sup>» — вставная статья 21-1, не «211» (реальный кейс УК 3-38)
        Assert.Equal(new[] { "21", "21-1", "22" }, law.Articles.Select(a => a.Number).ToArray());
        Assert.Equal("Рецидив преступлений", law.Articles[1].Title);
        // часть «2<sup>1</sup>.» внутри текста → «2-1.»
        Assert.Contains("2-1. Пожизненное лишение свободы", law.Articles[1].Text);
        Assert.Contains("части 2-1 настоящей статьи", law.Articles[1].Text);
        // якоря нормализованы: st_21_1 → "21-1"
        Assert.Equal(new[] { "21", "21-1", "22" }, law.AnchorArticleNumbers);
    }

    [Fact]
    public void Normalize_collapses_whitespace()
    {
        Assert.Equal("а б в", MinjustHtmlParser.Normalize("  а б\r\n  в­  "));
    }

    [Fact]
    public void Unicode_superscript_entity_becomes_hyphen_number()
    {
        // «Статья 240&sup1;. Срыв пломбы» — реальный кейс УК 3-38
        Assert.Equal("Статья 240-1. Срыв пломбы", MinjustHtmlParser.Normalize("Статья 240¹. Срыв пломбы"));
        // «м²» в таблицах штрафов не трогаем (нет цифры перед ²)
        Assert.Equal("площадью кв.м² больше", MinjustHtmlParser.Normalize("площадью кв.м² больше"));
    }

    [Fact]
    public void Cyrillic_letter_in_article_number_is_fixed()
    {
        // «Статья З09-3. …» с кириллической «З» — реальная опечатка КоП 3-36
        Assert.Equal("Статья 309-3. Нарушение требований",
            MinjustHtmlParser.Normalize("Статья З09-3. Нарушение требований"));
        // обычный текст со словом на «З» не трогаем
        Assert.Equal("Заявление подается в суд", MinjustHtmlParser.Normalize("Заявление подается в суд"));
    }
}