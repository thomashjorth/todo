import { renderMarkdown } from './render-markdown';

describe('renderMarkdown', () => {
  it('should turn emphasis into the elements that carry it', () => {
    const html = renderMarkdown('**fed** og *kursiv*');

    expect(html).toContain('<strong>fed</strong>');
    expect(html).toContain('<em>kursiv</em>');
  });

  it('should turn a bullet list into list items', () => {
    const html = renderMarkdown('- en\n- to');

    expect(html).toContain('<ul>');
    expect(html).toContain('<li>en</li>');
    expect(html).toContain('<li>to</li>');
  });

  it('should turn a numbered list into an ordered list', () => {
    const html = renderMarkdown('1. en\n2. to');

    expect(html).toContain('<ol>');
    expect(html).toContain('<li>en</li>');
  });

  it('should turn a task list into checkboxes', () => {
    const html = renderMarkdown('- [ ] ikke klar\n- [x] klar');

    expect(html).toContain('type="checkbox"');
    expect(html).toContain('checked');
  });

  it('should turn a link into an anchor with its href', () => {
    const html = renderMarkdown('[boardet](https://example.com/board)');

    expect(html).toContain('<a href="https://example.com/board"');
    expect(html).toContain('>boardet</a>');
  });

  it('should turn a fenced code block into pre and code', () => {
    const html = renderMarkdown('```\ndotnet test\n```');

    expect(html).toContain('<pre>');
    expect(html).toContain('<code');
    expect(html).toContain('dotnet test');
  });

  it('should turn a table into table elements', () => {
    const html = renderMarkdown('| a | b |\n| --- | --- |\n| 1 | 2 |');

    expect(html).toContain('<table>');
    expect(html).toContain('<th>a</th>');
    expect(html).toContain('<td>1</td>');
  });

  it('should turn a single newline into a line break', () => {
    const html = renderMarkdown('første linje\nanden linje');

    expect(html).toContain('<br>');
  });

  it.each([null, undefined, '', '   '])('should render nothing at all for %o', (source) => {
    expect(renderMarkdown(source)).toBe('');
  });

  // Deliberate: this function does not sanitise, Angular's [innerHTML] binding does. Nobody should
  // mistake it for a security layer.
  it('should leave a script tag in the output because sanitising is not its job', () => {
    const html = renderMarkdown('<script>alert(1)</script>');

    expect(html).toContain('<script>alert(1)</script>');
  });
});
