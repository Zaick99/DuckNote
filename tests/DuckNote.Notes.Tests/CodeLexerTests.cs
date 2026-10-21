using DuckNote.Notes.Code;

namespace DuckNote.Notes.Tests;

public class CodeLexerTests
{
    [Fact]
    public void Python_is_recognised_from_its_signs() =>
        Assert.Equal(CodeLanguage.Python, CodeLanguages.Guess(["def porte(host):", "    return None"]));

    [Fact]
    public void A_single_vague_sign_is_not_enough() =>
        Assert.Equal(CodeLanguage.Plain, CodeLanguages.Guess(["ciao $mondo"]));

    [Fact]
    public void Keywords_strings_numbers_comments_and_calls_get_their_ink()
    {
        CodeCarry carry = CodeCarry.None;
        IReadOnlyList<CodeSpan> spans = CodeLexer.Line("return bussa(host, 42)  # una alla volta", CodeLanguage.Python, ref carry);

        Assert.Contains(new CodeSpan("return", CodeInk.Keyword), spans);
        Assert.Contains(new CodeSpan("bussa", CodeInk.Call), spans);
        Assert.Contains(new CodeSpan("42", CodeInk.Number), spans);
        Assert.Equal(CodeInk.Comment, spans[^1].Ink);
        Assert.Equal("return bussa(host, 42)  # una alla volta", string.Concat(spans.Select(s => s.Text)));
    }

    [Fact]
    public void A_block_comment_left_open_carries_to_the_next_line()
    {
        IReadOnlyList<IReadOnlyList<CodeSpan>> rows = CodeLexer.Paint(
            ["using System;", "public class A { /* apre", "ancora commento */ void B() {} }"]);

        Assert.Equal(CodeInk.Comment, rows[2][0].Ink);
        Assert.Equal("ancora commento */", rows[2][0].Text);
    }

    [Fact]
    public void An_open_quote_does_not_tint_the_following_lines()
    {
        IReadOnlyList<IReadOnlyList<CodeSpan>> rows = CodeLexer.Paint(
            ["def a():", "    x = \"aperta", "    return None"]);

        Assert.Contains(new CodeSpan("return", CodeInk.Keyword), rows[2]);
    }

    [Fact]
    public void A_digit_inside_a_name_is_not_a_number()
    {
        CodeCarry carry = CodeCarry.None;
        IReadOnlyList<CodeSpan> spans = CodeLexer.Line("var x2 = 3;", CodeLanguage.CSharp, ref carry);

        Assert.DoesNotContain(spans, span => span.Ink == CodeInk.Number && span.Text.Contains('2'));
    }

    [Fact]
    public void Sql_keywords_ignore_case()
    {
        CodeCarry carry = CodeCarry.None;
        IReadOnlyList<CodeSpan> spans = CodeLexer.Line("select * FROM t", CodeLanguage.Sql, ref carry);

        Assert.Contains(new CodeSpan("select", CodeInk.Keyword), spans);
        Assert.Contains(new CodeSpan("FROM", CodeInk.Keyword), spans);
    }
}
