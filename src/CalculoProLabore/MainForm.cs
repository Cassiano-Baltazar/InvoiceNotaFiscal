using System.Text;
using InvoiceApp.Web.Services;

namespace CalculoProLabore;

/// <summary>
/// One window: the accountant types the month's revenue and sees the DAS, the pró-labore with its
/// INSS and IRRF, and the profit that can be distributed. The optional parameters below the revenue
/// (RBT12, Fator R, % of pró-labore, fee) only need changing when the month is not the usual one.
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly Font BaseFont = new("Segoe UI", 10f);
    private static readonly Font BoldFont = new("Segoe UI", 10f, FontStyle.Bold);
    private static readonly Font TitleFont = new("Segoe UI", 11f, FontStyle.Bold);
    private static readonly Font BigFont = new("Segoe UI", 16f, FontStyle.Bold);
    private static readonly Color Accent = Color.FromArgb(0, 92, 153);
    private static readonly Color Muted = Color.FromArgb(96, 96, 96);
    private static readonly Color Warning = Color.FromArgb(160, 82, 0);

    private readonly Preferences _preferences = Preferences.Load();

    private readonly DateTimePicker _month = new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "MM/yyyy",
        ShowUpDown = true,
        Width = 110
    };
    private readonly TextBox _revenue = NewInput(BigFont, 220);
    private readonly TextBox _rbt12 = NewInput(BaseFont, 160);
    private readonly TextBox _fatorR = NewInput(BaseFont, 80);
    private readonly TextBox _proLaboreRate = NewInput(BaseFont, 80);
    private readonly TextBox _accountantFee = NewInput(BaseFont, 120);
    private readonly CheckBox _export = new()
    {
        Text = "Exportação de serviços (sem ISS, PIS e COFINS no DAS)",
        AutoSize = true,
        Font = BaseFont
    };

    private readonly TableLayoutPanel _results = new()
    {
        ColumnCount = 2,
        AutoSize = true,
        Dock = DockStyle.Top,
        Padding = new Padding(0, 0, 8, 0)
    };
    private readonly Label _warnings = new()
    {
        AutoSize = true,
        MaximumSize = new Size(440, 0),
        ForeColor = Warning,
        Font = BaseFont,
        Dock = DockStyle.Top,
        Padding = new Padding(0, 12, 0, 0)
    };

    private WithdrawalCalculator.MonthSummary? _summary;

    public MainForm()
    {
        Text = "Cálculo de Pró-labore e Retirada Mensal — Simples Nacional";
        Font = BaseFont;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(980, 680);
        MinimumSize = new Size(900, 600);

        _month.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _revenue.Text = _preferences.Revenue;
        _rbt12.Text = _preferences.Rbt12;
        _fatorR.Text = _preferences.FatorR;
        _proLaboreRate.Text = _preferences.ProLaboreRate;
        _accountantFee.Text = _preferences.AccountantFee;
        _export.Checked = _preferences.Export;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(20)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.Controls.Add(BuildInputs(), 0, 0);
        root.Controls.Add(BuildResultsPanel(), 1, 0);
        Controls.Add(root);

        foreach (var box in new[] { _revenue, _rbt12, _fatorR, _proLaboreRate, _accountantFee })
        {
            box.TextChanged += (_, _) => Recalculate();
            box.Leave += (_, _) => Reformat((TextBox)box);
        }
        _export.CheckedChanged += (_, _) => Recalculate();
        _month.ValueChanged += (_, _) => Recalculate();
        FormClosing += (_, _) => SavePreferences();
        Shown += (_, _) => { _revenue.Focus(); _revenue.SelectAll(); };

        Recalculate();
    }

    private Control BuildInputs()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0, 0, 16, 0)
        };

        panel.Controls.Add(Title("Mês"));
        panel.Controls.Add(_month);

        panel.Controls.Add(Title("Faturamento do mês (R$)", top: 18));
        panel.Controls.Add(_revenue);
        panel.Controls.Add(Hint("Total das notas fiscais do mês, como vai no PGDAS-D."));

        var optional = new GroupBox
        {
            Text = "Parâmetros (opcional)",
            Font = BoldFont,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 22, 0, 0),
            Padding = new Padding(10)
        };
        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Font = BaseFont };
        AddField(grid, "Receita dos últimos 12 meses (RBT12)", _rbt12,
            "Vazio: usa o faturamento do mês × 12.");
        AddField(grid, "Fator R (%)", _fatorR,
            "Folha ÷ receita dos 12 meses anteriores. Vazio: Anexo III (Fator R de 28% ou mais).");
        AddField(grid, "Pró-labore (% do faturamento)", _proLaboreRate,
            "28% mantém o Fator R e o Anexo III.");
        AddField(grid, "Honorários contábeis (R$)", _accountantFee, null);
        grid.Controls.Add(_export, 0, grid.RowCount);
        grid.SetColumnSpan(_export, 2);
        grid.RowCount++;
        optional.Controls.Add(grid);
        panel.Controls.Add(optional);

        var copy = new Button
        {
            Text = "Copiar resumo",
            AutoSize = true,
            Padding = new Padding(10, 4, 10, 4),
            Margin = new Padding(0, 22, 0, 0),
            BackColor = Accent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        copy.FlatAppearance.BorderSize = 0;
        copy.Click += (_, _) => CopySummary();
        panel.Controls.Add(copy);
        panel.Controls.Add(Hint("Copia o resultado para colar no e-mail ou no WhatsApp."));

        panel.Controls.Add(Hint(
            "Tabelas de 2026: Simples Nacional (Anexos III e V), INSS 11% até o teto de R$ 8.475,55 " +
            "e IRRF com a redução da Lei 15.270/2025. Valores estimados: confira sempre com o PGDAS-D.",
            top: 22));
        return panel;
    }

    private Control BuildResultsPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        _results.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        _results.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        panel.Controls.Add(_warnings);
        panel.Controls.Add(_results);
        return panel;
    }

    private static TextBox NewInput(Font font, int width) => new()
    {
        Font = font,
        Width = width,
        TextAlign = HorizontalAlignment.Right
    };

    private static Label Title(string text, int top = 0) => new()
    {
        Text = text,
        Font = TitleFont,
        AutoSize = true,
        Margin = new Padding(0, top, 0, 4)
    };

    private static Label Hint(string text, int top = 0) => new()
    {
        Text = text,
        ForeColor = Muted,
        AutoSize = true,
        MaximumSize = new Size(420, 0),
        Margin = new Padding(0, top + 2, 0, 0),
        Font = new Font("Segoe UI", 9f)
    };

    private static void AddField(TableLayoutPanel grid, string label, TextBox box, string? hint)
    {
        var row = grid.RowCount;
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 8, 0) }, 0, row);
        box.Anchor = AnchorStyles.Left;
        box.Margin = new Padding(0, 6, 0, 0);
        grid.Controls.Add(box, 1, row);
        grid.RowCount++;
        if (hint is null)
            return;
        var note = Hint(hint);
        note.MaximumSize = new Size(380, 0);
        grid.Controls.Add(note, 0, grid.RowCount);
        grid.SetColumnSpan(note, 2);
        grid.RowCount++;
    }

    private static void Reformat(TextBox box)
    {
        if (Money.Parse(box.Text) is decimal value)
            box.Text = Money.Format(value);
    }

    private void Recalculate()
    {
        var revenue = Money.Parse(_revenue.Text) ?? 0m;
        var rbt12 = Money.Parse(_rbt12.Text);
        var fatorR = Money.Parse(_fatorR.Text) is decimal percent ? percent / 100m : (decimal?)null;
        var proLaboreRate = Money.Parse(_proLaboreRate.Text) ?? 28m;
        var fee = Money.Parse(_accountantFee.Text) ?? 0m;

        var s = WithdrawalCalculator.Summarize(revenue, rbt12, fatorR, proLaboreRate, fee, _export.Checked);
        _summary = s;

        _results.SuspendLayout();
        _results.Controls.Clear();
        _results.RowStyles.Clear();
        _results.RowCount = 0;

        Section("DAS — Simples Nacional");
        Row($"Anexo {s.Bracket.Anexo}, faixa {s.Bracket.Number}", "");
        Row("Receita dos últimos 12 meses (RBT12)" + (s.Rbt12FromRevenue ? " *" : ""), Money.Brl(s.Rbt12));
        Row("Alíquota efetiva", Money.Percent(s.DasRate, 4));
        Row("DAS a pagar", Money.Brl(s.Das), bold: true);

        Section($"Pró-labore ({s.ProLaboreRate.ToString("0.##", Money.PtBr)}% do faturamento)");
        Row("Pró-labore bruto", Money.Brl(s.ProLaboreGross), bold: true);
        Row("INSS (11%)", "− " + Money.Brl(s.Inss));
        Row("IRRF", "− " + Money.Brl(s.Irrf));
        Row("Guia única INSS + IRRF", Money.Brl(s.PayrollTaxes));
        Row("Pró-labore líquido", Money.Brl(s.ProLaboreNet), bold: true);

        Section("Distribuição de lucros");
        Row("Faturamento", Money.Brl(s.Revenue));
        Row("− DAS", Money.Brl(s.Das));
        Row("− Honorários contábeis", Money.Brl(s.AccountantFee));
        Row("− Pró-labore bruto", Money.Brl(s.ProLaboreGross));
        Row("Lucro distribuível (isento)", Money.Brl(s.Profit), bold: true, color: s.Profit < 0 ? Color.Firebrick : null);

        Section("Resumo");
        Row("Total que o sócio pode retirar", Money.Brl(s.TotalWithdrawal), big: true);
        Row("Pró-labore líquido + lucro", "");
        Row("Impostos do mês (DAS + guia INSS/IRRF)", Money.Brl(s.TaxReserve), bold: true);
        Row("Vencimento das guias", DueDate().ToString("dd/MM/yyyy"));

        _results.ResumeLayout();
        _warnings.Text = string.Join(Environment.NewLine + Environment.NewLine, Warnings(s));
    }

    private DateTime DueDate() => new DateTime(_month.Value.Year, _month.Value.Month, 20).AddMonths(1);

    private static IEnumerable<string> Warnings(WithdrawalCalculator.MonthSummary s)
    {
        if (s.Revenue <= 0)
        {
            yield return "Informe o faturamento do mês.";
            yield break;
        }
        if (s.Rbt12FromRevenue)
            yield return "* RBT12 estimado com o faturamento do mês × 12. Para o valor exato, informe a receita dos últimos 12 meses do PGDAS-D.";
        if (s.AnexoV)
            yield return $"Fator R de {Money.Percent(s.FatorR!.Value)} (abaixo de 28%): o DAS é calculado no Anexo V, bem mais caro.";
        else if (s.ProLaboreRate < 28m)
            yield return "Pró-labore abaixo de 28% do faturamento: o Fator R pode cair abaixo de 28% e levar a empresa ao Anexo V.";
        if (s.Profit < 0)
            yield return "O faturamento não cobre o DAS, os honorários e o pró-labore: não há lucro para distribuir.";
        if (s.Profit > 50_000m)
            yield return "Lucros acima de R$ 50.000 no mês têm 10% de IR retido na fonte (Lei 15.270/2025).";
        if (s.Rbt12 > WithdrawalCalculator.AnexoIII[^1].UpTo)
            yield return "A receita dos últimos 12 meses passa do limite do Simples Nacional (R$ 4,8 milhões).";
    }

    private void Section(string text)
    {
        var label = new Label
        {
            Text = text,
            Font = TitleFont,
            ForeColor = Accent,
            AutoSize = true,
            Margin = new Padding(0, _results.RowCount == 0 ? 0 : 16, 0, 4)
        };
        _results.Controls.Add(label, 0, _results.RowCount);
        _results.SetColumnSpan(label, 2);
        _results.RowCount++;
    }

    private void Row(string label, string value, bool bold = false, bool big = false, Color? color = null)
    {
        var font = big ? BigFont : bold ? BoldFont : BaseFont;
        _results.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Font = big ? BoldFont : font,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 8, 3),
            ForeColor = value.Length == 0 && !bold ? Muted : ForeColor
        }, 0, _results.RowCount);
        _results.Controls.Add(new Label
        {
            Text = value,
            AutoSize = true,
            Font = font,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 3, 0, 3),
            ForeColor = color ?? (big ? Accent : ForeColor)
        }, 1, _results.RowCount);
        _results.RowCount++;
    }

    private void CopySummary()
    {
        if (_summary is not { } s)
            return;
        var text = new StringBuilder()
            .AppendLine($"Retirada mensal — {_month.Value:MM/yyyy}")
            .AppendLine($"Faturamento: {Money.Brl(s.Revenue)}")
            .AppendLine()
            .AppendLine($"DAS (Anexo {s.Bracket.Anexo}, faixa {s.Bracket.Number}, alíquota efetiva {Money.Percent(s.DasRate, 4)}): {Money.Brl(s.Das)}")
            .AppendLine($"Pró-labore bruto ({s.ProLaboreRate.ToString("0.##", Money.PtBr)}%): {Money.Brl(s.ProLaboreGross)}")
            .AppendLine($"INSS (11%): {Money.Brl(s.Inss)}")
            .AppendLine($"IRRF: {Money.Brl(s.Irrf)}")
            .AppendLine($"Guia única INSS + IRRF: {Money.Brl(s.PayrollTaxes)}")
            .AppendLine($"Pró-labore líquido: {Money.Brl(s.ProLaboreNet)}")
            .AppendLine($"Honorários contábeis: {Money.Brl(s.AccountantFee)}")
            .AppendLine($"Lucro distribuível (isento): {Money.Brl(s.Profit)}")
            .AppendLine()
            .AppendLine($"Total que o sócio pode retirar: {Money.Brl(s.TotalWithdrawal)}")
            .AppendLine($"Impostos do mês (DAS + guia INSS/IRRF): {Money.Brl(s.TaxReserve)}")
            .AppendLine($"Vencimento das guias: {DueDate():dd/MM/yyyy}")
            .ToString();
        try
        {
            Clipboard.SetText(text);
            MessageBox.Show(this, "Resumo copiado.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não foi possível copiar: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SavePreferences()
    {
        _preferences.Revenue = _revenue.Text;
        _preferences.Rbt12 = _rbt12.Text;
        _preferences.FatorR = _fatorR.Text;
        _preferences.ProLaboreRate = _proLaboreRate.Text;
        _preferences.AccountantFee = _accountantFee.Text;
        _preferences.Export = _export.Checked;
        _preferences.Save();
    }
}
