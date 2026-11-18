using System.Text;
using InvoiceApp.Web.Services;

namespace CalculoProLabore;

/// <summary>
/// One window laid out like the Monthly Withdrawal tab of the InvoiceApp: the inputs on top
/// (revenue and RBT12 first), three summary cards, and the grids "Cálculo", "Pró-labore" and
/// "Para onde vai o dinheiro", followed by the DAS explanation.
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly Font BaseFont = new("Segoe UI", 9.75f);
    private static readonly Font BoldFont = new("Segoe UI", 9.75f, FontStyle.Bold);
    private static readonly Font SmallFont = new("Segoe UI", 8.25f);
    private static readonly Font SectionFont = new("Segoe UI", 13f, FontStyle.Bold);
    private static readonly Font PageTitleFont = new("Segoe UI", 18f, FontStyle.Bold);
    private static readonly Font CardValueFont = new("Segoe UI", 16f, FontStyle.Bold);
    private static readonly Font InputBigFont = new("Segoe UI", 12f, FontStyle.Bold);
    private static readonly Color PageBack = Color.FromArgb(243, 244, 246);
    private static readonly Color Border = Color.FromArgb(220, 222, 226);
    private static readonly Color Accent = Color.FromArgb(37, 99, 235);
    private static readonly Color Muted = Color.FromArgb(100, 100, 100);
    private static readonly Color Warning = Color.FromArgb(160, 82, 0);

    private readonly Preferences _preferences = Preferences.Load();

    private readonly DateTimePicker _month = new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "MM/yyyy",
        ShowUpDown = true,
        Width = 100
    };
    private readonly TextBox _revenue = NewInput(InputBigFont);
    private readonly TextBox _rbt12 = NewInput(InputBigFont);
    private readonly TextBox _fatorR = NewInput(BaseFont);
    private readonly TextBox _proLaboreRate = NewInput(BaseFont);
    private readonly TextBox _accountantFee = NewInput(BaseFont);
    private readonly CheckBox _export = new()
    {
        Text = "Exportação de serviços (sem ISS, PIS e COFINS no DAS)",
        AutoSize = true,
        Font = BaseFont,
        Margin = new Padding(0, 6, 0, 0)
    };

    private readonly Label _cardWithdrawal = CardValue();
    private readonly Label _cardTaxesCaption = CardCaption("Reservar para impostos");
    private readonly Label _cardTaxes = CardValue();
    private readonly Label _cardRevenue = CardValue();

    private readonly TableLayoutPanel _gridCalculation = NewGrid();
    private readonly TableLayoutPanel _gridProLabore = NewGrid();
    private readonly TableLayoutPanel _gridMoney = NewGrid();
    private readonly Label _payrollNote = Note("");
    private readonly Label _dasText = Note("");
    private readonly Label _warnings = new()
    {
        AutoSize = true,
        ForeColor = Warning,
        Font = BaseFont,
        Margin = new Padding(0, 10, 0, 0)
    };

    private WithdrawalCalculator.MonthSummary? _summary;

    public MainForm()
    {
        Text = "Cálculo de Pró-labore e Retirada Mensal — Simples Nacional";
        Font = BaseFont;
        BackColor = PageBack;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1280, 760);
        MinimumSize = new Size(1000, 640);

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
            ColumnCount = 3,
            AutoScroll = true,
            Padding = new Padding(20, 14, 20, 20),
            BackColor = PageBack
        };
        for (var i = 0; i < 3; i++)
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));

        AddSpanning(root, BuildHeader());
        AddSpanning(root, BuildInputs());

        AddRow(root,
            Card(CardCaption("Você pode retirar"), _cardWithdrawal),
            Card(_cardTaxesCaption, _cardTaxes),
            Card(CardCaption("Faturamento do mês"), _cardRevenue));
        AddRow(root, Section("Cálculo"), Section("Pró-labore"), Section("Para onde vai o dinheiro"));
        AddRow(root, _gridCalculation, WithNote(_gridProLabore, _payrollNote), _gridMoney);

        AddSpanning(root, Section("DAS"));
        _dasText.MaximumSize = new Size(1200, 0);
        AddSpanning(root, _dasText);
        _warnings.MaximumSize = new Size(1200, 0);
        AddSpanning(root, _warnings);
        AddSpanning(root, Note(
            "Tabelas de 2026: Simples Nacional (Anexos III e V), INSS 11% até o teto de R$ 8.475,55 e IRRF com a redução da " +
            "Lei 15.270/2025. Valores estimados: confira sempre com o PGDAS-D.", top: 16));

        Controls.Add(root);

        foreach (var box in new[] { _revenue, _rbt12, _fatorR, _proLaboreRate, _accountantFee })
        {
            box.TextChanged += (_, _) => Recalculate();
            box.Leave += (_, _) => Reformat(box);
        }
        _export.CheckedChanged += (_, _) => Recalculate();
        _month.ValueChanged += (_, _) => Recalculate();
        FormClosing += (_, _) => SavePreferences();
        Shown += (_, _) => { _revenue.Focus(); _revenue.SelectAll(); };

        Recalculate();
    }

    // ---------- layout ----------

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new Label { Text = "Retirada mensal", Font = PageTitleFont, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);

        var month = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Anchor = AnchorStyles.Right, WrapContents = false };
        month.Controls.Add(new Label { Text = "Mês", AutoSize = true, Margin = new Padding(0) });
        month.Controls.Add(_month);
        header.Controls.Add(month, 1, 0);
        return header;
    }

    private Control BuildInputs()
    {
        var card = new Panel
        {
            BackColor = Color.White,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 10, 14, 12),
            Margin = new Padding(0, 0, 0, 14)
        };
        card.Paint += PaintBorder;

        var grid = new TableLayoutPanel { ColumnCount = 6, AutoSize = true, Dock = DockStyle.Fill, BackColor = Color.White };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16));

        Field(grid, 0, "Faturamento do mês (R$)", _revenue, "Total das notas do mês, como vai no PGDAS-D.", bold: true);
        Field(grid, 1, "Receita dos últimos 12 meses — RBT12 (R$)", _rbt12, "Está no PGDAS-D. Vazio: faturamento × 12 (estimativa).", bold: true);
        Field(grid, 2, "Fator R (%)", _fatorR, "Vazio: Anexo III.");
        Field(grid, 3, "Pró-labore (% do faturamento)", _proLaboreRate, "28% mantém o Anexo III.");
        Field(grid, 4, "Honorários contábeis (R$)", _accountantFee, null);

        var copy = new Button
        {
            Text = "Copiar resumo",
            AutoSize = true,
            Padding = new Padding(10, 3, 10, 3),
            BackColor = Accent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            Margin = new Padding(8, 0, 0, 0)
        };
        copy.FlatAppearance.BorderSize = 0;
        copy.Click += (_, _) => CopySummary();
        grid.Controls.Add(copy, 5, 1);

        grid.Controls.Add(_export, 0, 3);
        grid.SetColumnSpan(_export, 6);

        card.Controls.Add(grid);
        return card;
    }

    private static void Field(TableLayoutPanel grid, int column, string label, TextBox box, string? hint, bool bold = false)
    {
        grid.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Font = bold ? BoldFont : BaseFont,
            Margin = new Padding(0, 0, 12, 4),
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom
        }, column, 0);
        box.Dock = DockStyle.Fill;
        box.Margin = new Padding(0, 0, 12, 0);
        grid.Controls.Add(box, column, 1);
        if (hint is not null)
            grid.Controls.Add(Note(hint, top: 2), column, 2);
    }

    private static TextBox NewInput(Font font) => new()
    {
        Font = font,
        TextAlign = HorizontalAlignment.Right,
        BorderStyle = BorderStyle.FixedSingle
    };

    private static void AddSpanning(TableLayoutPanel root, Control control)
    {
        var row = root.RowCount++;
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(control, 0, row);
        root.SetColumnSpan(control, 3);
    }

    private static void AddRow(TableLayoutPanel root, params Control[] controls)
    {
        var row = root.RowCount++;
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (var i = 0; i < controls.Length; i++)
        {
            controls[i].Margin = new Padding(i == 0 ? 0 : 8, controls[i].Margin.Top, i == controls.Length - 1 ? 0 : 8, controls[i].Margin.Bottom);
            root.Controls.Add(controls[i], i, row);
        }
    }

    private static Label CardCaption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Muted,
        Margin = new Padding(0, 0, 0, 4)
    };

    private static Label CardValue() => new()
    {
        AutoSize = true,
        Font = CardValueFont,
        Margin = new Padding(0)
    };

    private static Control Card(Label caption, Label value)
    {
        var card = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.White,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(12, 12, 12, 12),
            Margin = new Padding(0, 0, 0, 20)
        };
        card.Paint += PaintBorder;
        card.Controls.Add(caption);
        card.Controls.Add(value);
        return card;
    }

    private static void PaintBorder(object? sender, PaintEventArgs e)
    {
        var control = (Control)sender!;
        using var pen = new Pen(Border);
        e.Graphics.DrawRectangle(pen, 0, 0, control.Width - 1, control.Height - 1);
    }

    private static Label Section(string text) => new()
    {
        Text = text,
        Font = SectionFont,
        AutoSize = true,
        Margin = new Padding(0, 4, 0, 8)
    };

    private static Label Note(string text, int top = 4) => new()
    {
        Text = text,
        Font = SmallFont,
        ForeColor = Muted,
        AutoSize = true,
        MaximumSize = new Size(380, 0),
        Margin = new Padding(0, top, 0, 0)
    };

    private static Control WithNote(Control grid, Label note)
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill };
        grid.Margin = new Padding(0);
        panel.Controls.Add(grid);
        panel.Controls.Add(note);
        panel.SizeChanged += (_, _) => grid.Width = panel.ClientSize.Width;
        return panel;
    }

    private static TableLayoutPanel NewGrid()
    {
        var grid = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        return grid;
    }

    private static void FillGrid(TableLayoutPanel grid, IEnumerable<(string Label, string Value, bool Bold)> rows)
    {
        grid.SuspendLayout();
        grid.Controls.Clear();
        grid.RowStyles.Clear();
        grid.RowCount = 0;
        foreach (var (label, value, bold) in rows)
        {
            var row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(Cell(label, bold), 0, row);
            grid.Controls.Add(Cell(value, bold), 1, row);
        }
        grid.ResumeLayout();
    }

    private static Label Cell(string text, bool bold) => new()
    {
        Text = text,
        Font = bold ? BoldFont : BaseFont,
        AutoSize = true,
        Dock = DockStyle.Fill,
        Margin = new Padding(0),
        Padding = new Padding(8, 6, 8, 6)
    };

    // ---------- calculation ----------

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
        var rate = s.ProLaboreRate.ToString("0.##", Money.PtBr);

        _cardWithdrawal.Text = Money.Brl(s.TotalWithdrawal);
        _cardTaxesCaption.Text = $"Reservar para impostos (vence {DueDate():dd/MM/yyyy})";
        _cardTaxes.Text = Money.Brl(s.TaxReserve);
        _cardRevenue.Text = Money.Brl(s.Revenue);

        FillGrid(_gridCalculation,
        [
            ("Faturamento", Money.Brl(s.Revenue), true),
            ($"(−) DAS ({Money.Percent(s.DasRate)}{(s.Rbt12FromRevenue ? ", RBT12 estimado" : "")})", Money.Brl(s.Das), false),
            ("(−) Contador", Money.Brl(s.AccountantFee), false),
            ($"(−) Pró-labore bruto ({rate}%)", Money.Brl(s.ProLaboreGross), false),
            ("= Distribuição de lucros (isenta)", Money.Brl(s.Profit), true)
        ]);
        FillGrid(_gridProLabore,
        [
            ("Bruto", Money.Brl(s.ProLaboreGross), false),
            ("(−) INSS 11%", Money.Brl(s.Inss), false),
            ("(−) IRRF", Money.Brl(s.Irrf), false),
            ("Líquido", Money.Brl(s.ProLaboreNet), true)
        ]);
        _payrollNote.Text = $"INSS e IRRF são pagos juntos em uma guia ({Money.Brl(s.PayrollTaxes)}).";
        FillGrid(_gridMoney,
        [
            ("Pró-labore líquido", Money.Brl(s.ProLaboreNet), false),
            ("Distribuição de lucros", Money.Brl(Math.Max(0m, s.Profit)), false),
            ("Total a retirar", Money.Brl(s.TotalWithdrawal), true),
            ("Guia do DAS", Money.Brl(s.Das), false),
            ("Guia INSS + IRRF", Money.Brl(s.PayrollTaxes), false),
            ("Contador", Money.Brl(s.AccountantFee), false)
        ]);

        var fator = s.FatorR is decimal f ? $"Fator R {f.ToString("0.00", Money.PtBr)}" : "Fator R não informado";
        var exportText = _export.Checked
            ? $"Sem ISS, PIS e COFINS (serviços exportados) a alíquota é {Money.Percent(s.DasRate)} ({Money.Brl(s.Das)})."
            : $"Alíquota efetiva {Money.Percent(s.DasRate)} ({Money.Brl(s.Das)}).";
        _dasText.Text =
            $"Receita bruta dos últimos 12 meses (RBT12): {Money.Brl(s.Rbt12)}" +
            (s.Rbt12FromRevenue ? " (estimada: faturamento × 12)" : "") +
            $". {fator}: Anexo {s.Bracket.Anexo}, faixa {s.Bracket.Number}, alíquota nominal {s.Bracket.NominalRate.ToString("0.00", Money.PtBr)}%. " +
            exportText;

        _warnings.Text = string.Join(Environment.NewLine, Warnings(s));
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
            yield return "RBT12 estimado com o faturamento do mês × 12. Para o DAS exato, informe a receita dos últimos 12 meses que está no PGDAS-D.";
        if (s.AnexoV)
            yield return $"Fator R de {Money.Percent(s.FatorR!.Value)} (abaixo de 28%): o DAS é calculado no Anexo V, bem mais caro.";
        else if (s.ProLaboreRate < 28m)
            yield return "Pró-labore abaixo de 28% do faturamento: o Fator R pode cair abaixo de 28% e levar a empresa ao Anexo V.";
        if (s.Profit < 0)
            yield return "O faturamento não cobre o DAS, o contador e o pró-labore: não há lucro para distribuir.";
        if (s.Profit > 50_000m)
            yield return "Lucros acima de R$ 50.000 no mês têm 10% de IR retido na fonte (Lei 15.270/2025).";
        if (s.Rbt12 > WithdrawalCalculator.AnexoIII[^1].UpTo)
            yield return "A receita dos últimos 12 meses passa do limite do Simples Nacional (R$ 4,8 milhões).";
    }

    private void CopySummary()
    {
        if (_summary is not { } s)
            return;
        var text = new StringBuilder()
            .AppendLine($"Retirada mensal — {_month.Value:MM/yyyy}")
            .AppendLine($"Faturamento: {Money.Brl(s.Revenue)}")
            .AppendLine($"RBT12: {Money.Brl(s.Rbt12)}{(s.Rbt12FromRevenue ? " (estimado)" : "")}")
            .AppendLine()
            .AppendLine($"DAS (Anexo {s.Bracket.Anexo}, faixa {s.Bracket.Number}, {Money.Percent(s.DasRate)}): {Money.Brl(s.Das)}")
            .AppendLine($"Pró-labore bruto ({s.ProLaboreRate.ToString("0.##", Money.PtBr)}%): {Money.Brl(s.ProLaboreGross)}")
            .AppendLine($"INSS (11%): {Money.Brl(s.Inss)}")
            .AppendLine($"IRRF: {Money.Brl(s.Irrf)}")
            .AppendLine($"Guia INSS + IRRF: {Money.Brl(s.PayrollTaxes)}")
            .AppendLine($"Pró-labore líquido: {Money.Brl(s.ProLaboreNet)}")
            .AppendLine($"Contador: {Money.Brl(s.AccountantFee)}")
            .AppendLine($"Distribuição de lucros (isenta): {Money.Brl(s.Profit)}")
            .AppendLine()
            .AppendLine($"Total a retirar: {Money.Brl(s.TotalWithdrawal)}")
            .AppendLine($"Reservar para impostos: {Money.Brl(s.TaxReserve)} (vence {DueDate():dd/MM/yyyy})")
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
