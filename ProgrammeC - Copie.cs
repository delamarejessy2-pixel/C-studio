using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new StudioForm());
    }
}

internal sealed class Lesson
{
    public string Id;
    public string Title;
    public string Description;
    public string Code;
    public string[] BlockIds;

    public Lesson(string id, string title, string description, string code, string[] blockIds)
    {
        Id = id;
        Title = title;
        Description = description;
        Code = code;
        BlockIds = blockIds;
    }

    public override string ToString()
    {
        return Title;
    }
}

internal sealed class WordHelp
{
    public string Title;
    public string Description;

    public WordHelp(string title, string description)
    {
        Title = title;
        Description = description;
    }
}

internal sealed class HoverToken
{
    public string Text;
    public int Start;

    public HoverToken(string text, int start)
    {
        Text = text;
        Start = start;
    }
}

internal sealed class BlockDefinition
{
    public string Id;
    public string Title;
    public string Description;
    public string Code;
    public Color Color;

    public BlockDefinition(string id, string title, string description, string code, Color color)
    {
        Id = id;
        Title = title;
        Description = description;
        Code = code;
        Color = color;
    }
}

internal sealed class PlacedBlock
{
    public string DefinitionId;
    public int X;
    public int Y;

    public PlacedBlock(string definitionId, int x, int y)
    {
        DefinitionId = definitionId;
        X = x;
        Y = y;
    }
}

internal sealed class BlockHelpEventArgs : EventArgs
{
    public string Title;
    public string Description;
    public Point ScreenPosition;

    public BlockHelpEventArgs(string title, string description, Point screenPosition)
    {
        Title = title;
        Description = description;
        ScreenPosition = screenPosition;
    }
}

internal sealed class ProgramRunRequest
{
    public string CompilerPath;
    public string PowerShellPath;
    public string SourcePath;
    public string OutputPath;
    public string Input;
}

internal sealed class ProgramRunResult
{
    public string Output;
    public bool Cancelled;
    public string Summary;
}

internal sealed class StudioForm : Form
{
    private const int MaximumCustomWords = 30;
    private const int EmGetFirstVisibleLine = 0x00CE;
    private const int EmLineScroll = 0x00B6;
    private const int WmSetRedraw = 0x000B;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

    private readonly List<Lesson> lessons = new List<Lesson>();
    private readonly Dictionary<string, WordHelp> explanations = new Dictionary<string, WordHelp>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> customWords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BlockDefinition> blockDefinitions = new Dictionary<string, BlockDefinition>(StringComparer.OrdinalIgnoreCase);
    private readonly ComboBox lessonPicker = new ComboBox();
    private readonly RichTextBox editor = new RichTextBox();
    private readonly Label descriptionLabel = new Label();
    private readonly Label statusLabel = new Label();
    private readonly Label syncHintLabel = new Label();
    private Label codeHeading = new Label();
    private readonly TextBox runInput = new TextBox();
    private readonly TextBox runOutput = new TextBox();
    private readonly FlowLayoutPanel blockPalette = new FlowLayoutPanel();
    private readonly BlockCanvas blockCanvas = new BlockCanvas();
    private readonly HoverExplanation overlay = new HoverExplanation();
    private readonly Timer saveTimer = new Timer();
    private readonly string appFolder;
    private Button syncButton;
    private Button runButton;
    private Button stopButton;
    private BackgroundWorker runWorker;
    private Process activeProcess;
    private readonly object activeProcessLock = new object();
    private string currentFilePath;
    private bool sourceDirty;
    private bool ignoreLessonSelection;
    private Lesson currentLesson;
    private bool settingEditorText;
    private bool manualCodeChanges;
    private bool showWriteError;
    private bool hasHighlightedToken;
    private string lastHighlightedToken = "";
    private HoverToken lastToken;

    private readonly Color background = Color.FromArgb(15, 13, 36);
    private readonly Color surface = Color.FromArgb(29, 25, 59);
    private readonly Color edge = Color.FromArgb(70, 62, 110);
    private readonly Color paleText = Color.FromArgb(246, 239, 255);
    private readonly Color mutedText = Color.FromArgb(175, 165, 200);
    private readonly Color green = Color.FromArgb(119, 250, 222);
    private readonly Color neonPink = Color.FromArgb(255, 117, 191);
    private readonly Color arcadeYellow = Color.FromArgb(255, 218, 124);

    public StudioForm()
    {
        Text = "C Studio — apprendre le langage C";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1250, 720);
        Size = new Size(1440, 900);
        BackColor = background;
        ForeColor = paleText;
        Font = new Font("Segoe UI", 9f);
        KeyPreview = true;
        appFolder = Path.GetDirectoryName(Application.ExecutablePath);

        CreateLessons();
        CreateBlockDefinitions();
        CreateExplanations();
        CreateInterface();
        LoadGlossary();

        saveTimer.Interval = 550;
        saveTimer.Tick += delegate
        {
            saveTimer.Stop();
            SaveDraft();
        };

        lessonPicker.SelectedIndexChanged += OnLessonChanged;
        editor.TextChanged += OnEditorTextChanged;
        editor.MouseMove += OnEditorMouseMove;
        editor.MouseLeave += OnEditorMouseLeave;
        editor.VScroll += delegate { overlay.Hide(); };
        editor.HScroll += delegate { overlay.Hide(); };
        editor.KeyDown += OnEditorKeyDown;
        blockCanvas.DiagramChanged += OnDiagramChanged;
        blockCanvas.BlockHovered += OnBlockHovered;
        blockCanvas.BlockHoverEnded += OnBlockHoverEnded;
        ApplySyntaxColors();
        overlay.MouseLeave += delegate
        {
            Rectangle editorBounds = editor.RectangleToScreen(editor.ClientRectangle);
            if (!editorBounds.Contains(Cursor.Position))
            {
                overlay.Hide();
                hasHighlightedToken = false;
            }
        };

        lessonPicker.SelectedIndex = 0;

        FormClosed += delegate
        {
            saveTimer.Stop();
            if (currentLesson != null)
            {
                SaveDraft(currentLesson);
                SaveDiagram(currentLesson);
            }
            SaveGlossary();
            overlay.Dispose();
        };
        FormClosing += OnFormClosing;
    }

    private void CreateLessons()
    {
        lessons.Add(new Lesson(
            "bonjour",
            "01  ·  Afficher un message",
            "Le point de départ : demander au programme d’afficher une phrase.",
            "#include <stdio.h>\r\n\r\nint main(void)\r\n{\r\n    printf(\"Bonjour !\\n\");\r\n    return 0;\r\n}",
            new[] { "message" }));
        lessons.Add(new Lesson(
            "variable",
            "02  ·  Créer une variable",
            "Une variable garde une valeur que le programme peut réutiliser.",
            "#include <stdio.h>\r\n\r\nint main(void)\r\n{\r\n    int age = 18;\r\n    printf(\"J'ai %d ans.\\n\", age);\r\n    return 0;\r\n}",
            new[] { "age", "message-age" }));
        lessons.Add(new Lesson(
            "condition",
            "03  ·  Prendre une décision",
            "Une condition permet au programme de choisir les instructions à suivre.",
            "#include <stdio.h>\r\n\r\nint main(void)\r\n{\r\n    int age = 18;\r\n\r\n    if (age >= 18) {\r\n        printf(\"Tu es majeur.\\n\");\r\n    } else {\r\n        printf(\"Tu es mineur.\\n\");\r\n    }\r\n    return 0;\r\n}",
            new[] { "age", "condition" }));
    }

    private void CreateBlockDefinitions()
    {
        AddBlock("age", "Créer un entier « age »", "Range le nombre 18 dans une variable entière.", "int age = 18;", Color.FromArgb(77, 195, 255));
        AddBlock("message", "Afficher « Bonjour ! »", "Affiche une phrase dans la console.", "printf(\"Bonjour !\\n\");", Color.FromArgb(119, 250, 222));
        AddBlock("message-age", "Afficher la valeur de age", "Affiche la variable age dans une phrase.", "printf(\"J'ai %d ans.\\n\", age);", Color.FromArgb(119, 250, 222));
        AddBlock("condition", "Si age >= 18", "Prend la branche correspondante quand age vaut au moins 18.", "if (age >= 18)\r\n{\r\n    printf(\"Tu es majeur !\\n\");\r\n}", Color.FromArgb(214, 147, 255));
        AddBlock("increment", "Ajouter 1 à age", "Augmente la valeur de age d’une unité.", "age = age + 1;", Color.FromArgb(77, 195, 255));
        AddBlock("comment", "Ajouter un commentaire", "Note une idée : un commentaire n’est pas exécuté par le programme.", "// Mon idée à retenir", Color.FromArgb(255, 117, 191));
        AddBlock("read-age", "Lire un nombre", "Attend qu’une personne saisisse un entier et le range dans age.", "scanf(\"%d\", &age);", Color.FromArgb(255, 218, 124));
    }

    private void AddBlock(string id, string title, string description, string code, Color color)
    {
        blockDefinitions.Add(id, new BlockDefinition(id, title, description, code, color));
    }

    private void CreateExplanations()
    {
        AddHelp("#include", "Demande au compilateur d’ajouter une bibliothèque. stdio.h apporte les fonctions qui affichent du texte.");
        AddHelp("stdio.h", "Bibliothèque standard qui fournit notamment printf, la fonction qui affiche du texte.");
        AddHelp("int", "Type qui représente un nombre entier. Devant main, il indique également que le programme renverra un entier.");
        AddHelp("main", "Point de départ du programme : c’est cette fonction que l’ordinateur exécute en premier.");
        AddHelp("void", "Indique qu’une fonction ne reçoit aucun argument. Ici, main ne demande pas de valeur au démarrage.");
        AddHelp("printf", "Affiche du texte dans la console. Le message est entre guillemets et les valeurs à insérer suivent après une virgule.");
        AddHelp("scanf", "Attend une valeur saisie au clavier. Dans cet exemple, &age indique l’adresse de la variable à remplir.");
        AddHelp("return", "Termine cette fonction en renvoyant une valeur au système.");
        AddHelp("0", "À la fin de main, le nombre zéro indique généralement que le programme s’est terminé sans problème.");
        AddHelp("if", "Commence une condition : le code entre accolades qui suit ne s’exécute que si le test est vrai.");
        AddHelp("else", "Donne les instructions à suivre quand la condition du if est fausse.");
        AddHelp("age", "Nom choisi pour une variable : une case mémoire où le programme conserve une valeur.");
        AddHelp("18", "Valeur entière donnée à la variable age.");
        AddHelp("=", "Affectation : place la valeur de droite dans la variable située à gauche. Ce signe ne compare pas deux nombres.");
        AddHelp(">=", "Comparaison qui signifie « supérieur ou égal à ».");
        AddHelp("==", "Comparaison qui vérifie si deux valeurs sont égales. À ne pas confondre avec =, qui modifie une variable.");
        AddHelp("!=", "Comparaison qui signifie « différent de ».");
        AddHelp("&&", "Opérateur « ET » : les deux conditions doivent être vraies.");
        AddHelp("||", "Opérateur « OU » : au moins une des deux conditions doit être vraie.");
        AddHelp("%d", "Dans printf, indique l’endroit où insérer un nombre entier fourni après la virgule.");
        AddHelp("\\n", "Caractère spécial qui passe à la ligne suivante dans la console.");
        AddHelp("\"", "Délimite une chaîne de texte. Les guillemets ne sont pas eux-mêmes affichés.");
        AddHelp("(", "Ouvre la liste des arguments d’une fonction ou le test d’une condition.");
        AddHelp(")", "Ferme la liste des arguments d’une fonction ou le test d’une condition.");
        AddHelp("{", "Ouvre un bloc d’instructions. Une accolade fermante correspondante le terminera.");
        AddHelp("}", "Ferme le bloc d’instructions commencé par l’accolade ouvrante correspondante.");
        AddHelp(";", "Termine une instruction C. Un oubli fréquent quand on débute !");
        AddHelp(",", "Sépare les arguments d’une fonction. Ici, la virgule sépare le texte de la valeur à insérer.");
        AddHelp("+", "Additionne deux nombres, par exemple total = prix + taxe;");
        AddHelp("++", "Augmente une variable entière de 1 ; age++ revient à écrire age = age + 1;");
        AddHelp("-", "Soustrait le nombre à droite de celui de gauche.");
        AddHelp("*", "Multiplie deux nombres.");
        AddHelp("/", "Divise deux nombres.");
        AddHelp("%", "Calcule le reste d’une division entre deux entiers.");
        AddHelp("<", "Comparaison qui signifie « inférieur à ».");
        AddHelp("<=", "Comparaison qui signifie « inférieur ou égal à ».");
        AddHelp(">", "Comparaison qui signifie « supérieur à ».");
        AddHelp("!", "Signe de négation : !vrai signifie « faux », et inversement.");
        AddHelp("&", "Utilisé seul, cet opérateur peut manipuler les bits d’un entier. Dans &&, il signifie « ET » logique.");
        AddHelp("|", "Utilisé seul, cet opérateur peut manipuler les bits d’un entier. Dans ||, il signifie « OU » logique.");
        AddHelp("#", "Introduit une directive destinée au préprocesseur, comme #include.");
        AddHelp(".", "Sépare le nom d’une bibliothèque de son extension, comme stdio.h.");
    }

    private void AddHelp(string word, string definition)
    {
        explanations[word] = new WordHelp(word, definition);
    }

    private void CreateInterface()
    {
        TableLayoutPanel page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(22, 12, 22, 9),
            BackColor = background
        };
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 70f));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 73f));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 27f));
        Controls.Add(page);

        Panel header = new Panel { Dock = DockStyle.Fill };
        Label logo = new Label
        {
            Text = "C",
            Bounds = new Rectangle(1, 8, 41, 41),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.FromArgb(55, 42, 91),
            ForeColor = green,
            Font = new Font("Consolas", 22f, FontStyle.Bold)
        };
        Label title = new Label
        {
            Text = "C STUDIO",
            Location = new Point(54, 6),
            AutoSize = true,
            ForeColor = paleText,
            Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold)
        };
        Label subtitle = new Label
        {
            Text = "APPRENDS LE C EN ASSEMBLANT LES BLOCS",
            Location = new Point(55, 34),
            AutoSize = true,
            ForeColor = mutedText,
            Font = new Font("Consolas", 8f)
        };
        Label beginner = new Label
        {
            Text = "GLISSER  ·  RELIER  ·  COMPRENDRE",
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            ForeColor = green,
            BackColor = Color.FromArgb(49, 39, 84),
            Padding = new Padding(10, 6, 10, 6),
            Font = new Font("Consolas", 8f, FontStyle.Bold)
        };
        header.Resize += delegate { beginner.Location = new Point(header.ClientSize.Width - beginner.Width - 2, 14); };
        header.Controls.AddRange(new Control[] { logo, title, subtitle, beginner });
        page.Controls.Add(header, 0, 0);

        TableLayoutPanel toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(10, 7, 10, 6),
            BackColor = surface
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 25f));
        lessonPicker.DropDownStyle = ComboBoxStyle.DropDownList;
        lessonPicker.FlatStyle = FlatStyle.Flat;
        lessonPicker.Font = new Font("Segoe UI", 9f);
        lessonPicker.Dock = DockStyle.Fill;
        lessonPicker.BackColor = Color.FromArgb(41, 36, 76);
        lessonPicker.ForeColor = paleText;
        foreach (Lesson lesson in lessons)
        {
            lessonPicker.Items.Add(lesson);
        }

        FlowLayoutPanel toolbarButtons = new FlowLayoutPanel
        {
            Name = "GlossaryButtonBar",
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = surface
        };
        toolbarButtons.Controls.Add(CreateButton("Copier", false, OnCopyCode));
        toolbarButtons.Controls.Add(CreateButton("Réinitialiser", false, OnResetLesson));
        toolbarButtons.Controls.Add(CreateButton("Nouveau", false, OnNewFile));
        toolbarButtons.Controls.Add(CreateButton("Ouvrir", false, OnOpenFile));
        toolbarButtons.Controls.Add(CreateButton("Enregistrer", false, OnSaveFile));
        runButton = CreateButton("Exécuter", true, OnRunProgram);
        toolbarButtons.Controls.Add(runButton);
        stopButton = CreateButton("Arrêter", false, OnStopProgram);
        stopButton.Enabled = false;
        toolbarButtons.Controls.Add(stopButton);
        Button glossaryButton = CreateButton("Mes explications  ·  0", false, OnManageGlossary);
        glossaryButton.Name = "GlossaryButton";
        toolbarButtons.Controls.Add(glossaryButton);
        syncButton = CreateButton("Synchroniser le code", false, OnSyncCode);
        syncButton.Name = "SyncCodeButton";
        syncButton.Enabled = false;
        toolbarButtons.Controls.Add(syncButton);
        toolbar.Controls.Add(lessonPicker, 0, 0);
        toolbar.Controls.Add(toolbarButtons, 1, 0);
        toolbar.Controls.Add(descriptionLabel, 0, 1);
        toolbar.SetColumnSpan(descriptionLabel, 2);
        descriptionLabel.Dock = DockStyle.Fill;
        descriptionLabel.ForeColor = mutedText;
        descriptionLabel.TextAlign = ContentAlignment.MiddleLeft;
        descriptionLabel.AutoEllipsis = true;
        descriptionLabel.Font = new Font("Segoe UI", 8.5f);
        page.Controls.Add(toolbar, 0, 1);

        TableLayoutPanel workbench = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 9, 0, 0),
            BackColor = background
        };
        workbench.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
        workbench.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
        workbench.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        page.Controls.Add(workbench, 0, 2);

        TableLayoutPanel visualSide = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0, 0, 6, 0),
            BackColor = background
        };
        visualSide.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
        visualSide.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        Label visualHeading = new Label
        {
            Text = "BLOCS VISUELS",
            Dock = DockStyle.Fill,
            ForeColor = green,
            Font = new Font("Consolas", 9f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        TableLayoutPanel visualArea = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = background
        };
        visualArea.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176f));
        visualArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        visualArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        blockPalette.Dock = DockStyle.Fill;
        blockPalette.AutoScroll = true;
        blockPalette.WrapContents = false;
        blockPalette.FlowDirection = FlowDirection.TopDown;
        blockPalette.BackColor = surface;
        blockPalette.Padding = new Padding(7, 7, 5, 7);
        blockPalette.IsAccessible = true;
        blockPalette.AccessibleName = "Blocs C à glisser dans le quadrillage";
        blockPalette.AccessibleRole = AccessibleRole.Pane;
        blockPalette.Resize += OnPaletteResize;
        PopulatePalette();

        Panel canvasFrame = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            Margin = new Padding(6, 0, 0, 0),
            BackColor = Color.FromArgb(22, 19, 44)
        };
        blockCanvas.Dock = DockStyle.Fill;
        canvasFrame.Controls.Add(blockCanvas);
        visualArea.Controls.Add(blockPalette, 0, 0);
        visualArea.Controls.Add(canvasFrame, 1, 0);
        visualSide.Controls.Add(visualHeading, 0, 0);
        visualSide.Controls.Add(visualArea, 0, 1);
        workbench.Controls.Add(visualSide, 0, 0);

        TableLayoutPanel codeSide = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8, 0, 0, 0),
            BackColor = background
        };
        codeSide.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
        codeSide.RowStyles.Add(new RowStyle(SizeType.Percent, 59f));
        codeSide.RowStyles.Add(new RowStyle(SizeType.Percent, 41f));
        codeSide.RowStyles.Add(new RowStyle(SizeType.Absolute, 25f));
        codeHeading = new Label
        {
            Text = "CODE C  ·  MODIFIABLE",
            Dock = DockStyle.Fill,
            ForeColor = green,
            Font = new Font("Consolas", 9f, FontStyle.Bold),
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        Panel editorFrame = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(11),
            BackColor = Color.FromArgb(22, 19, 44)
        };
        editor.BorderStyle = BorderStyle.None;
        editor.Dock = DockStyle.Fill;
        editor.AcceptsTab = true;
        editor.DetectUrls = false;
        editor.HideSelection = false;
        editor.WordWrap = false;
        editor.Font = new Font("Consolas", 11f);
        editor.BackColor = Color.FromArgb(17, 15, 35);
        editor.ForeColor = Color.FromArgb(239, 233, 255);
        editor.Margin = new Padding(0);
        editor.ScrollBars = RichTextBoxScrollBars.Both;
        editorFrame.Controls.Add(editor);
        TableLayoutPanel consolePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(0, 8, 0, 3),
            BackColor = background
        };
        consolePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        consolePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        consolePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 21f));
        consolePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 27f));
        Label outputHeading = new Label
        {
            Text = "SORTIE DU PROGRAMME  ·  POWERSHELL",
            Dock = DockStyle.Fill,
            ForeColor = arcadeYellow,
            Font = new Font("Consolas", 8f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        runOutput.Multiline = true;
        runOutput.ReadOnly = true;
        runOutput.WordWrap = false;
        runOutput.ScrollBars = ScrollBars.Both;
        runOutput.Dock = DockStyle.Fill;
        runOutput.Font = new Font("Consolas", 9f);
        runOutput.BackColor = Color.FromArgb(17, 15, 35);
        runOutput.ForeColor = green;
        runOutput.BorderStyle = BorderStyle.FixedSingle;
        Label inputHeading = new Label
        {
            Text = "ENTRÉES CLAVIER POUR scanf (optionnel, une valeur par ligne)",
            Dock = DockStyle.Fill,
            ForeColor = mutedText,
            Font = new Font("Segoe UI", 8f),
            TextAlign = ContentAlignment.MiddleLeft
        };
        runInput.Dock = DockStyle.Fill;
        runInput.Multiline = true;
        runInput.AcceptsReturn = true;
        runInput.ScrollBars = ScrollBars.Vertical;
        runInput.Font = new Font("Consolas", 9f);
        runInput.BackColor = Color.FromArgb(41, 36, 76);
        runInput.ForeColor = paleText;
        runInput.BorderStyle = BorderStyle.FixedSingle;
        consolePanel.Controls.Add(outputHeading, 0, 0);
        consolePanel.Controls.Add(runOutput, 0, 1);
        consolePanel.Controls.Add(inputHeading, 0, 2);
        consolePanel.Controls.Add(runInput, 0, 3);
        syncHintLabel.Dock = DockStyle.Fill;
        syncHintLabel.ForeColor = mutedText;
        syncHintLabel.Font = new Font("Segoe UI", 8f);
        syncHintLabel.Text = "Survole les mots du code pour comprendre leur rôle.";
        codeSide.Controls.Add(codeHeading, 0, 0);
        codeSide.Controls.Add(editorFrame, 0, 1);
        codeSide.Controls.Add(consolePanel, 0, 2);
        codeSide.Controls.Add(syncHintLabel, 0, 3);
        workbench.Controls.Add(codeSide, 1, 0);

        Panel footer = new Panel { Dock = DockStyle.Fill };
        statusLabel.Text = "Maintiens le clic gauche sur un bloc, glisse-le dans la grille, puis relâche pour le déposer.";
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.ForeColor = mutedText;
        statusLabel.AutoEllipsis = true;
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusLabel.Font = new Font("Segoe UI", 8f);
        footer.Controls.Add(statusLabel);
        page.Controls.Add(footer, 0, 3);

        FormClosed += delegate { overlay.Hide(); };
    }

    private void PopulatePalette()
    {
        blockPalette.Controls.Clear();
        foreach (BlockDefinition definition in blockDefinitions.Values.OrderBy(item => item.Title, StringComparer.CurrentCulture))
        {
            CodeBlockControl tile = new CodeBlockControl(definition, true);
            tile.Size = new Size(150, 62);
            tile.Margin = new Padding(2, 3, 3, 5);
            tile.BlockHovered += OnBlockHovered;
            tile.BlockHoverEnded += OnBlockHoverEnded;
            blockPalette.Controls.Add(tile);
        }
    }

    private void OnPaletteResize(object sender, EventArgs e)
    {
        int width = Math.Max(122, blockPalette.ClientSize.Width - 22);
        foreach (Control control in blockPalette.Controls)
        {
            control.Width = width;
        }
    }

    private Button CreateButton(string title, bool primary, EventHandler action)
    {
        Button button = new Button
        {
            Text = title,
            AutoSize = true,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Color.FromArgb(55, 42, 91) : Color.FromArgb(35, 30, 66),
            ForeColor = primary ? green : paleText,
            Font = new Font("Segoe UI", 8.5f),
            Margin = new Padding(5, 0, 0, 0),
            Padding = new Padding(8, 0, 8, 0),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = primary ? neonPink : edge;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(81, 54, 112) : Color.FromArgb(54, 46, 91);
        button.Click += action;
        return button;
    }

    private void OnNewFile(object sender, EventArgs e)
    {
        if (!ConfirmDiscardOrSaveChanges())
        {
            return;
        }

        saveTimer.Stop();
        if (currentLesson != null)
        {
            SaveDraft(currentLesson);
            SaveDiagram(currentLesson);
        }

        currentLesson = null;
        currentFilePath = null;
        sourceDirty = false;
        manualCodeChanges = false;
        SetLessonSelection(-1);
        blockCanvas.SetBlocks(new PlacedBlock[0], blockDefinitions);
        SetEditorCode("");
        runOutput.Clear();
        descriptionLabel.Text = "Page vierge : écris ton programme C, puis enregistre-le en .c.";
        syncButton.Enabled = false;
        syncHintLabel.Text = "Nouvelle page C vierge  ·  rien ne sera remplacé par les blocs.";
        UpdateFileHeading();
        SetStatus("Page vierge prête. Enregistre ton fichier .c avant de le compiler et de l’exécuter.");
        ApplySyntaxColors();
    }

    private void OnOpenFile(object sender, EventArgs e)
    {
        using (OpenFileDialog dialog = new OpenFileDialog())
        {
            dialog.Title = "Ouvrir un programme C";
            dialog.Filter = "Fichiers C (*.c)|*.c|Tous les fichiers (*.*)|*.*";
            dialog.DefaultExt = "c";
            dialog.CheckFileExists = true;
            dialog.InitialDirectory = currentFilePath == null ? appFolder : Path.GetDirectoryName(currentFilePath);
            if (dialog.ShowDialog(this) != DialogResult.OK || !ConfirmDiscardOrSaveChanges())
            {
                return;
            }

            try
            {
                string code = File.ReadAllText(dialog.FileName, Encoding.UTF8);
                saveTimer.Stop();
                if (currentLesson != null)
                {
                    SaveDraft(currentLesson);
                    SaveDiagram(currentLesson);
                }

                currentLesson = null;
                SetLessonSelection(-1);
                blockCanvas.SetBlocks(new PlacedBlock[0], blockDefinitions);
                manualCodeChanges = false;
                currentFilePath = dialog.FileName;
                sourceDirty = false;
                SetEditorCode(code);
                descriptionLabel.Text = "Fichier ouvert : " + Path.GetFileName(currentFilePath);
                syncButton.Enabled = false;
                syncHintLabel.Text = "Modifie ton programme, puis enregistre-le avec Ctrl+S.";
                runOutput.Clear();
                UpdateFileHeading();
                ApplySyntaxColors();
                SetStatus("Fichier C ouvert. Enregistre tes changements avant de quitter ou d’ouvrir un autre fichier.");
            }
            catch (IOException error)
            {
                ShowFileError("ce fichier C", error);
            }
            catch (UnauthorizedAccessException error)
            {
                ShowFileError("ce fichier C", error);
            }
        }
    }

    private void OnSaveFile(object sender, EventArgs e)
    {
        SaveCurrentFile();
    }

    private bool SaveCurrentFile()
    {
        string path = currentFilePath;
        if (path == null)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Enregistrer le programme C";
                dialog.Filter = "Fichier C (*.c)|*.c";
                dialog.DefaultExt = "c";
                dialog.AddExtension = true;
                dialog.OverwritePrompt = true;
                dialog.InitialDirectory = appFolder;
                dialog.FileName = "nouveau-programme.c";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return false;
                }
                path = dialog.FileName;
            }
        }

        if (!String.Equals(Path.GetExtension(path), ".c", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                this,
                "Choisis un nom de fichier qui se termine par .c.",
                "Extension incorrecte",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        try
        {
            File.WriteAllText(path, editor.Text, new UTF8Encoding(false));
            currentFilePath = path;
            sourceDirty = false;
            UpdateFileHeading();
            SetStatus("Fichier enregistré : " + path);
            return true;
        }
        catch (IOException error)
        {
            ShowFileError("ce fichier C", error);
        }
        catch (UnauthorizedAccessException error)
        {
            ShowFileError("ce fichier C", error);
        }

        return false;
    }

    private bool ConfirmDiscardOrSaveChanges()
    {
        if (!sourceDirty || (currentFilePath == null && currentLesson != null))
        {
            return true;
        }

        DialogResult answer = MessageBox.Show(
            this,
            "Ce programme C contient des modifications non enregistrées. Veux-tu les enregistrer avant de continuer ?",
            "Modifications non enregistrées",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Warning);
        if (answer == DialogResult.Yes)
        {
            return SaveCurrentFile();
        }
        if (answer == DialogResult.No)
        {
            sourceDirty = false;
            return true;
        }
        return false;
    }

    private void SetLessonSelection(int index)
    {
        ignoreLessonSelection = true;
        try
        {
            lessonPicker.SelectedIndex = index;
        }
        finally
        {
            ignoreLessonSelection = false;
        }
    }

    private void RestoreLessonSelection()
    {
        ignoreLessonSelection = true;
        try
        {
            lessonPicker.SelectedIndex = currentLesson == null
                ? -1
                : lessonPicker.Items.IndexOf(currentLesson);
        }
        finally
        {
            ignoreLessonSelection = false;
        }
    }

    private void UpdateFileHeading()
    {
        string name = currentFilePath == null ? "NOUVEAU FICHIER" : Path.GetFileName(currentFilePath);
        codeHeading.Text = "CODE C  ·  " + name.ToUpperInvariant();
        Text = currentFilePath == null
            ? "C Studio — apprendre le langage C"
            : "C Studio — " + Path.GetFileName(currentFilePath);
    }

    private void OnRunProgram(object sender, EventArgs e)
    {
        if (runWorker != null && runWorker.IsBusy)
        {
            return;
        }
        if (!SaveCurrentFile())
        {
            return;
        }
        if (String.IsNullOrWhiteSpace(editor.Text))
        {
            runOutput.Text = "Écris d’abord un programme C avant de l’exécuter.";
            SetStatus("Le fichier est vide : ajoute du code C, puis réessaie.");
            return;
        }

        string compilerPath = FindCCompiler();
        if (compilerPath == null)
        {
            string message = "Compilateur C introuvable. Installe GCC (par exemple MinGW-w64), puis ajoute son dossier bin au PATH de Windows. Relance ensuite C Studio.";
            runOutput.Text = message;
            SetStatus("GCC manque : le programme a été enregistré, mais ne peut pas encore être compilé.");
            MessageBox.Show(this, message, "Compilateur C requis", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string powerShellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(powerShellPath))
        {
            string message = "Windows PowerShell est introuvable à son emplacement habituel. Le fichier C est enregistré, mais ne peut pas être lancé.";
            runOutput.Text = message;
            SetStatus("Impossible de trouver Windows PowerShell.");
            MessageBox.Show(this, message, "PowerShell introuvable", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        string outputDirectory = Path.Combine(Path.GetTempPath(), "CStudioRuns");
        string outputPath = Path.Combine(outputDirectory, Guid.NewGuid().ToString("N") + ".exe");
        ProgramRunRequest request = new ProgramRunRequest
        {
            CompilerPath = compilerPath,
            PowerShellPath = powerShellPath,
            SourcePath = Path.GetFullPath(currentFilePath),
            OutputPath = outputPath,
            Input = runInput.Text
        };
        runOutput.Clear();
        runButton.Enabled = false;
        stopButton.Enabled = true;
        runWorker = new BackgroundWorker
        {
            WorkerSupportsCancellation = true,
            WorkerReportsProgress = true
        };
        runWorker.DoWork += ExecuteProgramWorker;
        runWorker.ProgressChanged += OnProgramRunProgressChanged;
        runWorker.RunWorkerCompleted += OnProgramRunCompleted;
        runWorker.RunWorkerAsync(request);
        SetStatus("Compilation en cours avec GCC dans PowerShell…");
    }

    private string FindCCompiler()
    {
        string pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string directory in pathValue.Split(Path.PathSeparator))
        {
            string candidate = Path.Combine(directory.Trim(), "gcc.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        string[] commonDirectories =
        {
            Path.Combine(appFolder, "mingw64", "bin"),
            Path.Combine(appFolder, "gcc", "bin"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "mingw-w64", "bin"),
            @"C:\msys64\mingw64\bin",
            @"C:\msys64\ucrt64\bin",
            @"C:\mingw64\bin",
            @"C:\MinGW\bin"
        };
        foreach (string directory in commonDirectories)
        {
            string candidate = Path.Combine(directory, "gcc.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    private void ExecuteProgramWorker(object sender, DoWorkEventArgs e)
    {
        ProgramRunRequest request = (ProgramRunRequest)e.Argument;
        BackgroundWorker worker = (BackgroundWorker)sender;
        StringBuilder output = new StringBuilder();
        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath));
        AppendAndReport(worker, output, "=== Compilation ===");
        int compilationExitCode = RunPowerShell(
            request,
            "& $env:CSTUDIO_COMPILER -std=c11 -Wall -Wextra -o $env:CSTUDIO_OUTPUT_EXE $env:CSTUDIO_SOURCE_FILE; exit $LASTEXITCODE",
            "",
            output,
            worker);

        if (worker.CancellationPending)
        {
            e.Result = new ProgramRunResult
            {
                Output = output.ToString(),
                Cancelled = true,
                Summary = "Exécution arrêtée."
            };
            return;
        }
        if (compilationExitCode != 0)
        {
            e.Result = new ProgramRunResult
            {
                Output = output.ToString(),
                Summary = "La compilation a échoué. Lis les messages de GCC dans la sortie."
            };
            return;
        }

        AppendAndReport(worker, output, "");
        AppendAndReport(worker, output, "=== Exécution ===");
        int executionExitCode = RunPowerShell(
            request,
            "& $env:CSTUDIO_OUTPUT_EXE; exit $LASTEXITCODE",
            request.Input,
            output,
            worker);
        if (worker.CancellationPending)
        {
            e.Result = new ProgramRunResult
            {
                Output = output.ToString(),
                Cancelled = true,
                Summary = "Exécution arrêtée."
            };
            return;
        }

        e.Result = new ProgramRunResult
        {
            Output = output.ToString(),
            Summary = executionExitCode == 0
                ? "Programme terminé."
                : "Le programme s’est terminé avec le code d’erreur " + executionExitCode + "."
        };
    }

    private int RunPowerShell(
        ProgramRunRequest request,
        string script,
        string input,
        StringBuilder output,
        BackgroundWorker worker)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = request.PowerShellPath,
            Arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(request.SourcePath)
        };
        startInfo.EnvironmentVariables["CSTUDIO_COMPILER"] = request.CompilerPath;
        startInfo.EnvironmentVariables["CSTUDIO_SOURCE_FILE"] = request.SourcePath;
        startInfo.EnvironmentVariables["CSTUDIO_OUTPUT_EXE"] = request.OutputPath;
        startInfo.EnvironmentVariables["PATH"] =
            Path.GetDirectoryName(request.CompilerPath) + Path.PathSeparator +
            (Environment.GetEnvironmentVariable("PATH") ?? "");

        using (Process process = new Process { StartInfo = startInfo })
        {
            process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args)
            {
                if (args.Data != null)
                {
                    AppendAndReport(worker, output, args.Data);
                }
            };
            process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args)
            {
                if (args.Data != null)
                {
                    AppendAndReport(worker, output, args.Data);
                }
            };
            process.Start();
            lock (activeProcessLock)
            {
                activeProcess = process;
                if (worker.CancellationPending)
                {
                    TryStopProcess(process);
                }
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            try
            {
                if (!String.IsNullOrEmpty(input))
                {
                    process.StandardInput.Write(input);
                    if (!input.EndsWith("\n", StringComparison.Ordinal))
                    {
                        process.StandardInput.WriteLine();
                    }
                }
            }
            catch (IOException)
            {
                if (!process.HasExited)
                {
                    throw;
                }
            }
            finally
            {
                process.StandardInput.Close();
            }
            process.WaitForExit();
            process.WaitForExit();
            lock (activeProcessLock)
            {
                if (activeProcess == process)
                {
                    activeProcess = null;
                }
            }
            return process.ExitCode;
        }
    }

    private static void AppendOutput(StringBuilder output, string line)
    {
        lock (output)
        {
            if (output.Length > 18000)
            {
                return;
            }
            output.AppendLine(line);
        }
    }

    private static void AppendAndReport(BackgroundWorker worker, StringBuilder output, string line)
    {
        AppendOutput(output, line);
        if (worker.WorkerReportsProgress)
        {
            worker.ReportProgress(0, line);
        }
    }

    private void OnProgramRunProgressChanged(object sender, ProgressChangedEventArgs e)
    {
        string line = e.UserState as string;
        if (line == null || runOutput.TextLength >= 18000)
        {
            return;
        }
        runOutput.AppendText(line + Environment.NewLine);
        runOutput.SelectionStart = runOutput.TextLength;
        runOutput.ScrollToCaret();
    }

    private static void TryStopProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void OnStopProgram(object sender, EventArgs e)
    {
        if (runWorker == null || !runWorker.IsBusy)
        {
            return;
        }
        runWorker.CancelAsync();
        lock (activeProcessLock)
        {
            if (activeProcess != null)
            {
                TryStopProcess(activeProcess);
            }
        }
        SetStatus("Arrêt demandé…");
    }

    private void OnProgramRunCompleted(object sender, RunWorkerCompletedEventArgs e)
    {
        runButton.Enabled = true;
        stopButton.Enabled = false;
        if (e.Error != null)
        {
            runOutput.AppendText(Environment.NewLine + "Erreur pendant la compilation ou l’exécution :\r\n" + e.Error.Message);
            SetStatus("La compilation ou l’exécution n’a pas pu se terminer correctement.");
        }
        else if (e.Cancelled)
        {
            SetStatus("Exécution arrêtée.");
        }
        else
        {
            ProgramRunResult result = (ProgramRunResult)e.Result;
            runOutput.Text = result.Output;
            SetStatus(result.Cancelled ? "Exécution arrêtée." : result.Summary);
        }

        if (closingAfterRun)
        {
            allowClose = true;
            Close();
        }
    }

    private bool closingAfterRun;
    private bool allowClose;

    private void OnFormClosing(object sender, FormClosingEventArgs e)
    {
        if (allowClose)
        {
            return;
        }
        if (runWorker != null && runWorker.IsBusy)
        {
            DialogResult answer = MessageBox.Show(
                this,
                "Un programme est en cours. Veux-tu l’arrêter et fermer C Studio ?",
                "Exécution en cours",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            closingAfterRun = true;
            e.Cancel = true;
            OnStopProgram(this, EventArgs.Empty);
            return;
        }
        if (!ConfirmDiscardOrSaveChanges())
        {
            e.Cancel = true;
        }
    }

    private void OnLessonChanged(object sender, EventArgs e)
    {
        if (ignoreLessonSelection)
        {
            return;
        }

        Lesson lesson = lessonPicker.SelectedItem as Lesson;
        if (lesson == null)
        {
            return;
        }

        if (!ConfirmDiscardOrSaveChanges())
        {
            RestoreLessonSelection();
            return;
        }

        saveTimer.Stop();
        if (currentLesson != null && currentLesson != lesson)
        {
            SaveDraft(currentLesson);
            SaveDiagram(currentLesson);
        }

        currentLesson = lesson;
        currentFilePath = null;
        sourceDirty = false;
        UpdateFileHeading();
        descriptionLabel.Text = lesson.Description;
        if (!LoadDiagram(lesson))
        {
            LoadExampleBlocks(lesson);
        }

        string generatedCode = blockCanvas.BuildCode();
        string savedCode = generatedCode;
        bool hasSavedCode = false;
        try
        {
            string path = GetDraftPath(lesson);
            if (File.Exists(path))
            {
                savedCode = File.ReadAllText(path, Encoding.UTF8);
                hasSavedCode = true;
            }
        }
        catch (IOException error)
        {
            ShowFileError("le brouillon", error);
        }
        catch (UnauthorizedAccessException error)
        {
            ShowFileError("le brouillon", error);
        }

        manualCodeChanges = hasSavedCode && !CodesMatch(savedCode, generatedCode);
        SetEditorCode(savedCode);
        syncButton.Enabled = manualCodeChanges;
        syncHintLabel.Text = manualCodeChanges
            ? "Code saisi à la main : synchronise-le si tu veux utiliser les blocs."
            : "Survole les mots du code pour comprendre leur rôle.";
        ApplySyntaxColors();
        SetStatus(manualCodeChanges
            ? "Ton code écrit à la main est gardé. Il ne sera remplacé que si tu demandes une synchronisation."
            : "Glisse un bloc sur la grille : les fils indiquent l’ordre d’exécution et le code C se met à jour.");
    }

    private void LoadExampleBlocks(Lesson lesson)
    {
        List<PlacedBlock> blocks = new List<PlacedBlock>();
        for (int index = 0; index < lesson.BlockIds.Length; index++)
        {
            blocks.Add(new PlacedBlock(lesson.BlockIds[index], 18, 18 + index * 96));
        }
        blockCanvas.SetBlocks(blocks, blockDefinitions);
    }

    private bool LoadDiagram(Lesson lesson)
    {
        string path = GetDiagramPath(lesson);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            List<PlacedBlock> blocks = new List<PlacedBlock>();
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                string[] values = line.Split('\t');
                int x;
                int y;
                if (values.Length != 3 ||
                    !blockDefinitions.ContainsKey(values[0]) ||
                    !Int32.TryParse(values[1], out x) ||
                    !Int32.TryParse(values[2], out y) ||
                    x < 0 || y < 0)
                {
                    throw new FormatException("Une ligne du quadrillage est illisible.");
                }

                blocks.Add(new PlacedBlock(values[0], x, y));
            }

            blockCanvas.SetBlocks(blocks, blockDefinitions);
            return true;
        }
        catch (IOException error)
        {
            ShowFileError("le quadrillage de cet exemple", error);
            return false;
        }
        catch (UnauthorizedAccessException error)
        {
            ShowFileError("le quadrillage de cet exemple", error);
            return false;
        }
        catch (FormatException error)
        {
            ShowFileError("le quadrillage de cet exemple", error);
            return false;
        }
    }

    private void SaveDiagram(Lesson lesson)
    {
        if (lesson == null || IsDisposed || blockCanvas.IsDisposed)
        {
            return;
        }

        try
        {
            List<string> lines = new List<string>();
            foreach (PlacedBlock block in blockCanvas.GetBlocks())
            {
                lines.Add(String.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0}\t{1}\t{2}",
                    block.DefinitionId,
                    block.X,
                    block.Y));
            }
            File.WriteAllLines(GetDiagramPath(lesson), lines, new UTF8Encoding(false));
        }
        catch (IOException error)
        {
            HandleWriteError("le quadrillage de cet exemple", error);
        }
        catch (UnauthorizedAccessException error)
        {
            HandleWriteError("le quadrillage de cet exemple", error);
        }
    }

    private string GetDiagramPath(Lesson lesson)
    {
        return Path.Combine(appFolder, "quadrillage-" + lesson.Id + ".txt");
    }

    private static bool CodesMatch(string first, string second)
    {
        return String.Equals(
            (first ?? "").Replace("\r\n", "\n").TrimEnd('\n'),
            (second ?? "").Replace("\r\n", "\n").TrimEnd('\n'),
            StringComparison.Ordinal);
    }

    private void SetEditorCode(string code)
    {
        settingEditorText = true;
        try
        {
            editor.Text = code;
        }
        finally
        {
            settingEditorText = false;
        }
    }

    private void OnDiagramChanged(object sender, EventArgs e)
    {
        SaveDiagram(currentLesson);
        if (!manualCodeChanges)
        {
            SetEditorCode(blockCanvas.BuildCode());
            if (currentFilePath != null)
            {
                sourceDirty = true;
            }
            saveTimer.Stop();
            SaveDraft(currentLesson);
            syncButton.Enabled = false;
            syncHintLabel.Text = "Code généré à partir des blocs : modifie le code ou déplace les blocs.";
            return;
        }

        SetStatus("Les blocs ont changé. Ton code écrit à la main reste intact ; synchronise-le si tu veux le remplacer.");
    }

    private void OnEditorTextChanged(object sender, EventArgs e)
    {
        if (settingEditorText)
        {
            ApplySyntaxColors();
            return;
        }

        saveTimer.Stop();
        saveTimer.Start();
        sourceDirty = true;
        ApplySyntaxColors();
        manualCodeChanges = !CodesMatch(editor.Text, blockCanvas.BuildCode());
        syncButton.Enabled = manualCodeChanges;
        syncHintLabel.Text = manualCodeChanges
            ? "Code modifié à la main : les blocs ne le remplaceront pas sans ton accord."
            : "Ton texte correspond au programme représenté par les blocs.";
        SetStatus(String.Format(
            manualCodeChanges ? "Code modifié à la main  ·  {0} ligne{1}" : "Code synchronisé  ·  {0} ligne{1}",
            editor.Lines.Length,
            editor.Lines.Length == 1 ? "" : "s"));
        overlay.Hide();
        hasHighlightedToken = false;
    }

    private void ApplySyntaxColors()
    {
        if (editor.IsDisposed || editor.TextLength == 0)
        {
            return;
        }

        int firstVisibleLine = (int)SendMessage(editor.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero);
        int selectionStart = editor.SelectionStart;
        int selectionLength = editor.SelectionLength;
        SendMessage(editor.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
        editor.SelectAll();
        editor.SelectionColor = editor.ForeColor;
        string code = editor.Text;
        MatchCollection matches = Regex.Matches(
            code,
            @"""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|//[^\r\n]*|/\*[\s\S]*?\*/|^\s*#\s*\w+|\b(?:int|char|float|double|void|long|short|unsigned|signed|const|return|if|else|while|for|do|break|continue|switch|case|default|struct|typedef|enum|static|sizeof)\b|\b(?:printf|scanf|main|strlen|strcpy|malloc|free)\b|\b\d+(?:\.\d+)?\b",
            RegexOptions.Multiline);

        foreach (Match match in matches)
        {
            editor.Select(match.Index, match.Length);
            if (match.Value.StartsWith("//", StringComparison.Ordinal) || match.Value.StartsWith("/*", StringComparison.Ordinal))
            {
                editor.SelectionColor = Color.FromArgb(119, 193, 166);
            }
            else if (match.Value.StartsWith("\"", StringComparison.Ordinal) || match.Value.StartsWith("'", StringComparison.Ordinal))
            {
                editor.SelectionColor = arcadeYellow;
            }
            else if (match.Value.IndexOf("#", StringComparison.Ordinal) >= 0)
            {
                editor.SelectionColor = Color.FromArgb(214, 147, 255);
            }
            else if (Regex.IsMatch(match.Value, @"^\d"))
            {
                editor.SelectionColor = Color.FromArgb(255, 184, 123);
            }
            else if (Regex.IsMatch(match.Value, @"^(printf|scanf|main|strlen|strcpy|malloc|free)$"))
            {
                editor.SelectionColor = Color.FromArgb(77, 195, 255);
            }
            else
            {
                editor.SelectionColor = green;
            }
        }

        editor.Select(selectionStart, selectionLength);
        int currentFirstLine = (int)SendMessage(editor.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero);
        if (currentFirstLine != firstVisibleLine)
        {
            SendMessage(editor.Handle, EmLineScroll, IntPtr.Zero, new IntPtr(firstVisibleLine - currentFirstLine));
        }
        SendMessage(editor.Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
        editor.Invalidate();
    }

    private void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.S)
        {
            e.SuppressKeyPress = true;
            saveTimer.Stop();
            if (currentFilePath != null || currentLesson == null)
            {
                SaveCurrentFile();
            }
            else
            {
                SaveDraft();
                SaveDiagram(currentLesson);
            }
        }
    }

    private void SaveDraft()
    {
        SaveDraft(currentLesson);
    }

    private void SaveDraft(Lesson lesson)
    {
        if (lesson == null || IsDisposed)
        {
            return;
        }

        try
        {
            File.WriteAllText(GetDraftPath(lesson), editor.Text, new UTF8Encoding(false));
            if (currentLesson == lesson && currentFilePath == null)
            {
                sourceDirty = false;
            }
            if (showWriteError)
            {
                showWriteError = false;
                SetStatus("Brouillon enregistré  ·  fichier gardé à côté du programme.");
            }
        }
        catch (IOException error)
        {
            HandleWriteError("le brouillon", error);
        }
        catch (UnauthorizedAccessException error)
        {
            HandleWriteError("le brouillon", error);
        }
    }

    private string GetDraftPath(Lesson lesson)
    {
        return Path.Combine(appFolder, "brouillon-" + lesson.Id + ".c");
    }

    private void LoadGlossary()
    {
        string path = Path.Combine(appFolder, "mon-glossaire.txt");
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (String.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] fields = line.Split('\t');
                if (fields.Length != 2)
                {
                    throw new FormatException("Une ligne du glossaire ne contient pas les deux champs attendus.");
                }

                string term = Encoding.UTF8.GetString(Convert.FromBase64String(fields[0]));
                string definition = Encoding.UTF8.GetString(Convert.FromBase64String(fields[1]));
                if (!String.IsNullOrWhiteSpace(term) && !String.IsNullOrWhiteSpace(definition) &&
                    !customWords.ContainsKey(term) && customWords.Count < MaximumCustomWords)
                {
                    customWords.Add(term, definition);
                }
            }
        }
        catch (IOException error)
        {
            ShowFileError("ton glossaire personnel", error);
        }
        catch (UnauthorizedAccessException error)
        {
            ShowFileError("ton glossaire personnel", error);
        }
        catch (FormatException error)
        {
            ShowFileError("ton glossaire personnel", error);
        }
        catch (ArgumentException error)
        {
            ShowFileError("ton glossaire personnel", error);
        }

        UpdateGlossaryButton();
    }

    private bool SaveGlossary()
    {
        string path = Path.Combine(appFolder, "mon-glossaire.txt");
        try
        {
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, string> entry in customWords)
            {
                string term = Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.Key));
                string definition = Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.Value));
                lines.Add(term + "\t" + definition);
            }

            File.WriteAllLines(path, lines, new UTF8Encoding(false));
            return true;
        }
        catch (IOException error)
        {
            HandleWriteError("ton glossaire personnel", error);
            return false;
        }
        catch (UnauthorizedAccessException error)
        {
            HandleWriteError("ton glossaire personnel", error);
            return false;
        }
    }

    private void OnManageGlossary(object sender, EventArgs e)
    {
        using (GlossaryForm glossary = new GlossaryForm(customWords, MaximumCustomWords))
        {
            if (glossary.ShowDialog(this) == DialogResult.OK)
            {
                customWords.Clear();
                foreach (KeyValuePair<string, string> entry in glossary.Entries)
                {
                    customWords.Add(entry.Key, entry.Value);
                }

                if (SaveGlossary())
                {
                    SetStatus("Glossaire enregistré  ·  passe la souris sur tes mots pour les retrouver.");
                }
                UpdateGlossaryButton();
            }
        }
    }

    private void UpdateGlossaryButton()
    {
        Button button = Controls.Find("GlossaryButton", true).FirstOrDefault() as Button;
        if (button != null)
        {
            button.Text = String.Format("Mes explications  ·  {0}", customWords.Count);
        }
    }

    private void OnResetLesson(object sender, EventArgs e)
    {
        Lesson lesson = lessonPicker.SelectedItem as Lesson;
        if (lesson == null)
        {
            return;
        }

        DialogResult answer = MessageBox.Show(
            this,
            "Remplacer le brouillon de cet exemple par le code de départ ?",
            "Réinitialiser l’exemple",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        saveTimer.Stop();
        LoadExampleBlocks(lesson);
        manualCodeChanges = false;
        SetEditorCode(blockCanvas.BuildCode());
        sourceDirty = currentFilePath != null;
        syncButton.Enabled = false;
        SaveDraft(lesson);
        SaveDiagram(lesson);
        SetStatus("Blocs et code réinitialisés pour reprendre cet exemple depuis le début.");
    }

    private void OnSyncCode(object sender, EventArgs e)
    {
        if (!manualCodeChanges)
        {
            return;
        }

        DialogResult answer = MessageBox.Show(
            this,
            "Remplacer ton texte saisi à la main par le code produit par les blocs ?",
            "Synchroniser le code",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        saveTimer.Stop();
        manualCodeChanges = false;
        SetEditorCode(blockCanvas.BuildCode());
        sourceDirty = currentFilePath != null;
        syncButton.Enabled = false;
        syncHintLabel.Text = "Code généré à partir des blocs : modifie le code ou déplace les blocs.";
        SaveDraft(currentLesson);
        SetStatus("Code synchronisé : il correspond maintenant au quadrillage.");
    }

    private void OnCopyCode(object sender, EventArgs e)
    {
        try
        {
            Clipboard.SetText(editor.Text);
            SetStatus("Code copié dans le presse-papiers  ·  tu peux le coller dans un fichier .c.");
        }
        catch (ExternalException error)
        {
            MessageBox.Show(
                this,
                "Windows n’a pas pu accéder au presse-papiers. Sélectionne le code puis utilise Ctrl+C.\r\n\r\n" + error.Message,
                "Copie indisponible",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        catch (System.Threading.ThreadStateException error)
        {
            MessageBox.Show(this, error.Message, "Copie indisponible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnEditorMouseMove(object sender, MouseEventArgs e)
    {
        int characterIndex = editor.GetCharIndexFromPosition(e.Location);
        if (characterIndex < 0 || characterIndex >= editor.TextLength)
        {
            overlay.Hide();
            return;
        }

        HoverToken token = FindTokenAt(characterIndex);
        if (token == null)
        {
            overlay.Hide();
            hasHighlightedToken = false;
            return;
        }

        if (hasHighlightedToken && token.Text == lastHighlightedToken && lastToken != null && lastToken.Start == token.Start)
        {
            return;
        }

        hasHighlightedToken = true;
        lastHighlightedToken = token.Text;
        lastToken = token;

        WordHelp help;
        string label = "NOTION DE C";
        string customDefinition;
        if (customWords.TryGetValue(token.Text, out customDefinition))
        {
            help = new WordHelp(token.Text, customDefinition);
            label = "TON EXPLICATION PERSONNELLE";
        }
        else if (!explanations.TryGetValue(token.Text, out help))
        {
            help = new WordHelp(token.Text, "Pas encore d’explication pour ce mot. Clique sur « Mes explications » en haut pour l’ajouter à ton glossaire.");
            label = "MOT À AJOUTER À TON GLOSSAIRE";
        }

        Point screenPosition = editor.PointToScreen(e.Location);
        overlay.Display(help.Title, help.Description, label, screenPosition, this);
    }

    private HoverToken FindTokenAt(int characterIndex)
    {
        string code = editor.Text;
        if (characterIndex >= code.Length)
        {
            return null;
        }

        if (Char.IsWhiteSpace(code[characterIndex]))
        {
            return null;
        }

        int lineStart = code.LastIndexOf('\n', characterIndex);
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        int lineEnd = code.IndexOf('\n', characterIndex);
        lineEnd = lineEnd < 0 ? code.Length : lineEnd;

        int directiveStart = lineStart;
        while (directiveStart < lineEnd && (code[directiveStart] == ' ' || code[directiveStart] == '\t'))
        {
            directiveStart++;
        }

        if (directiveStart + 8 <= lineEnd &&
            String.Compare(code, directiveStart, "#include", 0, 8, StringComparison.Ordinal) == 0 &&
            characterIndex >= directiveStart && characterIndex < directiveStart + 8)
        {
            return new HoverToken("#include", directiveStart);
        }

        foreach (string formattingCode in new[] { "%d", "\\n" })
        {
            int formattingPosition = code.IndexOf(formattingCode, lineStart, lineEnd - lineStart, StringComparison.Ordinal);
            if (formattingPosition >= 0 && characterIndex >= formattingPosition &&
                characterIndex < formattingPosition + formattingCode.Length)
            {
                return new HoverToken(formattingCode, formattingPosition);
            }
        }

        int libraryPosition = code.IndexOf("stdio.h", lineStart, lineEnd - lineStart, StringComparison.Ordinal);
        if (libraryPosition >= 0 && characterIndex >= libraryPosition && characterIndex < libraryPosition + 7)
        {
            return new HoverToken("stdio.h", libraryPosition);
        }

        if (Char.IsLetterOrDigit(code[characterIndex]) || code[characterIndex] == '_')
        {
            int start = characterIndex;
            int end = characterIndex + 1;
            while (start > lineStart && IsIdentifierCharacter(code[start - 1])) start--;
            while (end < lineEnd && IsIdentifierCharacter(code[end])) end++;
            return new HoverToken(code.Substring(start, end - start), start);
        }

        int tokenStart = characterIndex;
        string[] operators = { "<<=", ">>=", "==", "!=", ">=", "<=", "&&", "||", "++", "--", "+=", "-=", "*=", "/=", "%=", "<<", ">>" };
        foreach (string op in operators)
        {
            int start = Math.Max(lineStart, characterIndex - op.Length + 1);
            int end = Math.Min(lineEnd, characterIndex + op.Length);
            for (int candidate = start; candidate < end; candidate++)
            {
                if (candidate + op.Length <= lineEnd &&
                    String.Compare(code, candidate, op, 0, op.Length, StringComparison.Ordinal) == 0 &&
                    characterIndex >= candidate && characterIndex < candidate + op.Length)
                {
                    tokenStart = candidate;
                    return new HoverToken(op, tokenStart);
                }
            }
        }

        return new HoverToken(code[characterIndex].ToString(), tokenStart);
    }

    private static bool IsIdentifierCharacter(char value)
    {
        return Char.IsLetterOrDigit(value) || value == '_';
    }

    private void OnEditorMouseLeave(object sender, EventArgs e)
    {
        Point cursor = Cursor.Position;
        if (!overlay.Bounds.Contains(cursor))
        {
            overlay.Hide();
            hasHighlightedToken = false;
        }
    }

    private void OnBlockHovered(object sender, BlockHelpEventArgs e)
    {
        overlay.Display(e.Title, e.Description, "BLOC DE PROGRAMMATION", e.ScreenPosition, this);
    }

    private void OnBlockHoverEnded(object sender, EventArgs e)
    {
        if (!overlay.Bounds.Contains(Cursor.Position))
        {
            overlay.Hide();
            hasHighlightedToken = false;
        }
    }

    private void ShowFileError(string fileDescription, Exception error)
    {
        MessageBox.Show(
            this,
            String.Format("Impossible de lire {0} à côté de CStudio.exe.\r\n\r\n{1}", fileDescription, error.Message),
            "Erreur de lecture",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private void HandleWriteError(string fileDescription, Exception error)
    {
        SetStatus("Sauvegarde impossible  ·  vérifie que ta clé USB n’est pas protégée en écriture.");
        if (showWriteError)
        {
            return;
        }

        showWriteError = true;
        MessageBox.Show(
            this,
            String.Format("Impossible d’enregistrer {0} à côté de CStudio.exe. Vérifie que le dossier est accessible en écriture et que la clé USB n’est pas verrouillée.\r\n\r\n{1}", fileDescription, error.Message),
            "Sauvegarde impossible",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private void SetStatus(string text)
    {
        statusLabel.Text = text;
    }
}

internal sealed class HoverExplanation : Form
{
    private readonly Label category = new Label();
    private readonly Label title = new Label();
    private readonly Label definition = new Label();

    public HoverExplanation()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(35, 30, 70);
        Padding = new Padding(12, 9, 12, 10);
        Size = new Size(330, 112);

        category.Dock = DockStyle.Top;
        category.Height = 17;
        category.ForeColor = Color.FromArgb(119, 250, 222);
        category.Font = new Font("Consolas", 8f, FontStyle.Bold);

        title.Dock = DockStyle.Top;
        title.Height = 25;
        title.ForeColor = Color.FromArgb(246, 239, 255);
        title.Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold);
        title.AutoEllipsis = true;

        definition.Dock = DockStyle.Fill;
        definition.ForeColor = Color.FromArgb(215, 208, 234);
        definition.Font = new Font("Segoe UI", 9f);
        definition.AutoEllipsis = true;

        Controls.Add(definition);
        Controls.Add(title);
        Controls.Add(category);
        Paint += delegate(object sender, PaintEventArgs e)
        {
            using (Pen border = new Pen(Color.FromArgb(255, 117, 191)))
            {
                e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            }
        };
    }

    protected override bool ShowWithoutActivation
    {
        get { return true; }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000 | 0x00000080;
            return parameters;
        }
    }

    public void Display(string word, string explanation, string label, Point cursor, Form owner)
    {
        title.Text = word;
        definition.Text = explanation;
        category.Text = label;

        const int offset = 18;
        Point position = new Point(cursor.X + offset, cursor.Y + offset);
        Screen screen = Screen.FromPoint(cursor);
        if (position.X + Width > screen.WorkingArea.Right)
        {
            position.X = cursor.X - Width - offset;
        }
        if (position.Y + Height > screen.WorkingArea.Bottom)
        {
            position.Y = cursor.Y - Height - offset;
        }
        position.X = Math.Max(screen.WorkingArea.Left, Math.Min(position.X, screen.WorkingArea.Right - Width));
        position.Y = Math.Max(screen.WorkingArea.Top, Math.Min(position.Y, screen.WorkingArea.Bottom - Height));
        Location = position;

        if (!Visible)
        {
            Show(owner);
        }
    }
}

internal sealed class GlossaryForm : Form
{
    private readonly Dictionary<string, string> entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly int maximumWords;
    private readonly TextBox termInput = new TextBox();
    private readonly TextBox definitionInput = new TextBox();
    private readonly ListBox wordList = new ListBox();
    private readonly Label message = new Label();
    private readonly Label count = new Label();
    private readonly Color light = Color.FromArgb(246, 239, 255);
    private readonly Color subdued = Color.FromArgb(175, 165, 200);
    private readonly Color dark = Color.FromArgb(17, 15, 35);
    private readonly Color panel = Color.FromArgb(29, 25, 59);
    private Button addButton;

    public IDictionary<string, string> Entries
    {
        get { return new Dictionary<string, string>(entries, StringComparer.OrdinalIgnoreCase); }
    }

    public GlossaryForm(IDictionary<string, string> savedEntries, int wordLimit)
    {
        maximumWords = wordLimit;
        foreach (KeyValuePair<string, string> entry in savedEntries)
        {
            entries.Add(entry.Key, entry.Value);
        }

        Text = "Mes explications personnelles";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(512, 498);
        BackColor = Color.FromArgb(15, 13, 36);
        ForeColor = light;
        Font = new Font("Segoe UI", 9f);

        BuildControls();
        RenderEntries();
    }

    private void BuildControls()
    {
        Label heading = CreateLabel("TON GLOSSAIRE PERSONNEL", 20, 17, 240, 20, Color.FromArgb(119, 250, 222), 8f);
        Label title = CreateLabel("Ajoute les mots à ta façon.", 20, 38, 450, 32, light, 15f);
        Label instructions = CreateLabel("Ces deux cases suffisent : ton explication apparaîtra en survolant ce mot dans le code.", 20, 74, 470, 33, subdued, 9f);
        Controls.AddRange(new Control[] { heading, title, instructions });

        Controls.Add(CreateLabel("LE MOT OU LE SYMBOLE", 20, 118, 240, 16, light, 8f));
        termInput.Bounds = new Rectangle(20, 136, 472, 31);
        StyleInput(termInput);
        termInput.MaxLength = 40;

        Controls.Add(CreateLabel("TON EXPLICATION", 20, 177, 240, 16, light, 8f));
        definitionInput.Bounds = new Rectangle(20, 196, 472, 64);
        StyleInput(definitionInput);
        definitionInput.Multiline = true;
        definitionInput.AcceptsReturn = true;
        definitionInput.ScrollBars = ScrollBars.Vertical;
        definitionInput.MaxLength = 240;

        addButton = new Button
        {
            Text = "＋  Ajouter mon explication",
            Bounds = new Rectangle(20, 272, 205, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(55, 42, 91),
            ForeColor = Color.FromArgb(119, 250, 222),
            Font = new Font("Segoe UI", 9f),
            Cursor = Cursors.Hand
        };
        addButton.FlatAppearance.BorderColor = Color.FromArgb(255, 117, 191);
        addButton.Click += OnAddEntry;

        Button removeButton = new Button
        {
            Text = "Supprimer le mot choisi",
            Bounds = new Rectangle(325, 271, 167, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(35, 30, 66),
            ForeColor = light,
            Font = new Font("Segoe UI", 8.5f)
        };
        removeButton.FlatAppearance.BorderColor = Color.FromArgb(70, 62, 110);
        removeButton.Click += OnRemoveEntry;

        count.Bounds = new Rectangle(20, 322, 280, 18);
        count.ForeColor = subdued;
        count.Font = new Font("Consolas", 8f);

        wordList.Bounds = new Rectangle(20, 343, 472, 101);
        wordList.BackColor = dark;
        wordList.ForeColor = light;
        wordList.BorderStyle = BorderStyle.FixedSingle;
        wordList.Font = new Font("Segoe UI", 9f);
        wordList.HorizontalScrollbar = true;

        message.Bounds = new Rectangle(20, 452, 295, 27);
        message.ForeColor = Color.FromArgb(119, 250, 222);
        message.Font = new Font("Segoe UI", 8f);
        message.AutoEllipsis = true;

        Button saveButton = new Button
        {
            Text = "Enregistrer",
            Bounds = new Rectangle(414, 450, 78, 32),
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(55, 42, 91),
            ForeColor = Color.FromArgb(119, 250, 222),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };
        saveButton.FlatAppearance.BorderColor = Color.FromArgb(255, 117, 191);

        Controls.AddRange(new Control[] { termInput, definitionInput, addButton, removeButton, count, wordList, message, saveButton });
        AcceptButton = saveButton;
        Button cancelButton = new Button
        {
            Text = "Annuler",
            Bounds = new Rectangle(326, 450, 78, 32),
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(35, 30, 66),
            ForeColor = light,
            Font = new Font("Segoe UI", 8.5f)
        };
        cancelButton.FlatAppearance.BorderColor = Color.FromArgb(70, 62, 110);
        Controls.Add(cancelButton);
        Controls.SetChildIndex(cancelButton, Controls.Count - 1);
        CancelButton = cancelButton;
    }

    private Label CreateLabel(string text, int x, int y, int width, int height, Color color, float size)
    {
        return new Label
        {
            Text = text,
            Bounds = new Rectangle(x, y, width, height),
            ForeColor = color,
            Font = new Font("Segoe UI", size, size > 12 ? FontStyle.Bold : FontStyle.Regular),
            AutoEllipsis = true
        };
    }

    private void StyleInput(TextBox input)
    {
        input.BackColor = dark;
        input.ForeColor = light;
        input.BorderStyle = BorderStyle.FixedSingle;
        input.Font = new Font("Segoe UI", 9f);
    }

    private void OnAddEntry(object sender, EventArgs e)
    {
        string term = termInput.Text.Trim();
        string definition = definitionInput.Text.Trim();
        if (term.Length == 0 || definition.Length == 0)
        {
            SetMessage("Remplis les deux cases avant d’ajouter.", true);
            return;
        }

        if (entries.ContainsKey(term))
        {
            SetMessage("Ce mot existe déjà. Supprime-le pour le remplacer.", true);
            return;
        }

        if (entries.Count >= maximumWords)
        {
            SetMessage("Le glossaire est plein. Supprime d’abord un mot.", true);
            return;
        }

        entries.Add(term, definition);
        termInput.Clear();
        definitionInput.Clear();
        RenderEntries();
        termInput.Focus();
        SetMessage("Explication ajoutée ! Clique sur Enregistrer pour la garder sur la clé.", false);
    }

    private void OnRemoveEntry(object sender, EventArgs e)
    {
        string selected = wordList.SelectedItem as string;
        if (selected == null)
        {
            SetMessage("Choisis un mot dans la liste avant de le supprimer.", true);
            return;
        }

        entries.Remove(selected);
        RenderEntries();
        SetMessage("Mot supprimé. Clique sur Enregistrer pour confirmer.", false);
    }

    private void RenderEntries()
    {
        wordList.Items.Clear();
        foreach (string term in entries.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            wordList.Items.Add(term);
        }

        count.Text = String.Format("MES MOTS  ·  {0} / {1}", entries.Count, maximumWords);
        addButton.Enabled = entries.Count < maximumWords;
    }

    private void SetMessage(string text, bool isError)
    {
        message.ForeColor = isError ? Color.FromArgb(255, 157, 144) : Color.FromArgb(119, 250, 222);
        message.Text = text;
    }
}

internal sealed class CodeBlockControl : Control
{
    private Point mouseDownPoint;
    private bool dragStarted;

    public BlockDefinition Definition { get; private set; }
    public bool IsPaletteItem { get; private set; }

    public event EventHandler<BlockHelpEventArgs> BlockHovered;
    public event EventHandler BlockHoverEnded;

    public CodeBlockControl(BlockDefinition definition, bool paletteItem)
    {
        Definition = definition;
        IsPaletteItem = paletteItem;
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);
        BackColor = Color.FromArgb(35, 30, 70);
        ForeColor = Color.FromArgb(246, 239, 255);
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = definition.Title;
        AccessibleDescription = definition.Description;
        IsAccessible = true;
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (SolidBrush fill = new SolidBrush(BackColor))
        using (SolidBrush stripe = new SolidBrush(Definition.Color))
        using (SolidBrush socket = new SolidBrush(Color.FromArgb(22, 19, 44)))
        using (Pen border = new Pen(Definition.Color, IsPaletteItem ? 1f : 1.4f))
        using (Font titleFont = new Font("Segoe UI Semibold", IsPaletteItem ? 8.5f : 9f, FontStyle.Bold))
        using (Font detailFont = new Font("Segoe UI", 7.5f))
        using (Font removeFont = new Font("Segoe UI", 11f))
        {
            Rectangle bounds = new Rectangle(1, 1, Width - 3, Height - 3);
            graphics.FillRectangle(fill, bounds);
            graphics.FillRectangle(stripe, bounds.Left, bounds.Top, 4, bounds.Height);
            graphics.DrawRectangle(border, bounds);

            int textLeft = bounds.Left + 12;
            int textWidth = Math.Max(40, bounds.Width - (IsPaletteItem ? 22 : 40));
            TextRenderer.DrawText(
                graphics,
                Definition.Title,
                titleFont,
                new Rectangle(textLeft, bounds.Top + 8, textWidth, 22),
                ForeColor,
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

            string detail = IsPaletteItem ? Definition.Description : GetCodePreview(Definition.Code);
            TextRenderer.DrawText(
                graphics,
                detail,
                detailFont,
                new Rectangle(textLeft, bounds.Top + 30, textWidth, Height - 34),
                Color.FromArgb(205, 195, 226),
                TextFormatFlags.EndEllipsis | TextFormatFlags.WordBreak);

            if (!IsPaletteItem)
            {
                graphics.FillEllipse(socket, Width / 2 - 5, -1, 10, 10);
                graphics.DrawEllipse(border, Width / 2 - 5, -1, 10, 10);
                graphics.FillEllipse(socket, Width / 2 - 5, Height - 9, 10, 10);
                graphics.DrawEllipse(border, Width / 2 - 5, Height - 9, 10, 10);
                TextRenderer.DrawText(
                    graphics,
                    "×",
                    removeFont,
                    new Rectangle(Width - 25, 1, 21, 21),
                    Color.FromArgb(255, 117, 191),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    private static string GetCodePreview(string code)
    {
        string compact = code.Replace("\r", "").Replace("\n", "  ");
        return compact.Length > 42 ? compact.Substring(0, 39) + "..." : compact;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            mouseDownPoint = e.Location;
            dragStarted = false;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsPaletteItem && e.Button == MouseButtons.Left && !dragStarted &&
            Math.Abs(e.X - mouseDownPoint.X) + Math.Abs(e.Y - mouseDownPoint.Y) >= 5)
        {
            dragStarted = true;
            DoDragDrop(Definition.Id, DragDropEffects.Copy);
        }

        EventHandler<BlockHelpEventArgs> handler = BlockHovered;
        if (handler != null)
        {
            handler(this, new BlockHelpEventArgs(
                Definition.Title,
                Definition.Description,
                PointToScreen(e.Location)));
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        EventHandler handler = BlockHoverEnded;
        if (handler != null)
        {
            handler(this, EventArgs.Empty);
        }
    }
}

internal sealed class BlockCanvas : Panel
{
    private const int GridSize = 24;
    private const int BlockHeight = 68;
    private const int MaximumBlockWidth = 248;
    private readonly Color gridLine = Color.FromArgb(48, 42, 79);
    private IDictionary<string, BlockDefinition> blockDefinitions;
    private bool loadingBlocks;
    private CodeBlockControl draggedBlock;
    private Point dragOffset;

    public event EventHandler DiagramChanged;
    public event EventHandler<BlockHelpEventArgs> BlockHovered;
    public event EventHandler BlockHoverEnded;

    public BlockCanvas()
    {
        AutoScroll = true;
        AllowDrop = true;
        BackColor = Color.FromArgb(17, 15, 35);
        BorderStyle = BorderStyle.FixedSingle;
        AccessibleRole = AccessibleRole.Pane;
        AccessibleName = "Quadrillage du programme. Dépose et déplace les blocs ici.";
        IsAccessible = true;
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        Resize += delegate { UpdateBlockWidths(); };
        AutoScrollMinSize = new Size(MaximumBlockWidth + GridSize * 2, 0);
    }

    public void SetBlocks(IEnumerable<PlacedBlock> blocks, IDictionary<string, BlockDefinition> definitions)
    {
        blockDefinitions = definitions;
        loadingBlocks = true;
        Controls.Clear();
        foreach (PlacedBlock block in blocks)
        {
            BlockDefinition definition;
            if (definitions.TryGetValue(block.DefinitionId, out definition))
            {
                AddPlacedBlock(definition, new Point(block.X, block.Y));
            }
        }

        UpdateCanvasSize();
        loadingBlocks = false;
        Invalidate();
    }

    public List<PlacedBlock> GetBlocks()
    {
        List<PlacedBlock> blocks = new List<PlacedBlock>();
        foreach (CodeBlockControl tile in OrderedBlocks())
        {
            blocks.Add(new PlacedBlock(tile.Definition.Id, tile.Left, tile.Top));
        }
        return blocks;
    }

    public string BuildCode()
    {
        StringBuilder code = new StringBuilder();
        code.Append("#include <stdio.h>\r\n\r\nint main(void)\r\n{\r\n");
        foreach (CodeBlockControl block in OrderedBlocks())
        {
            string[] lines = block.Definition.Code.Replace("\r\n", "\n").Split('\n');
            foreach (string line in lines)
            {
                if (line.Length != 0)
                {
                    code.Append("    ").Append(line.TrimEnd('\r'));
                }
                code.Append("\r\n");
            }
        }
        code.Append("    return 0;\r\n}");
        return code.ToString();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        Point scroll = AutoScrollPosition;
        using (Pen pen = new Pen(gridLine, 1f))
        {
            int startX = ((scroll.X % GridSize) + GridSize) % GridSize;
            int startY = ((scroll.Y % GridSize) + GridSize) % GridSize;
            for (int x = startX; x < ClientSize.Width; x += GridSize)
            {
                e.Graphics.DrawLine(pen, x, 0, x, ClientSize.Height);
            }
            for (int y = startY; y < ClientSize.Height; y += GridSize)
            {
                e.Graphics.DrawLine(pen, 0, y, ClientSize.Width, y);
            }
        }

        if (!Controls.OfType<CodeBlockControl>().Any())
        {
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(175, 165, 200)))
            using (Font font = new Font("Segoe UI", 9f))
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    "Maintiens le clic gauche sur un bloc,\r\npuis relâche-le dans ce quadrillage.",
                    font,
                    new Rectangle(15, 16, Math.Max(140, ClientSize.Width - 30), 42),
                    brush.Color,
                    TextFormatFlags.WordBreak);
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        List<CodeBlockControl> blocks = OrderedBlocks();
        if (blocks.Count < 2)
        {
            return;
        }

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        Point scroll = AutoScrollPosition;
        using (Pen shadow = new Pen(Color.FromArgb(85, 8, 6, 24), 6f))
        using (Pen wire = new Pen(Color.FromArgb(119, 250, 222), 2.5f))
        {
            for (int index = 0; index < blocks.Count - 1; index++)
            {
                CodeBlockControl current = blocks[index];
                CodeBlockControl next = blocks[index + 1];
                Point start = new Point(
                    current.Left + current.Width / 2 + scroll.X,
                    current.Bottom + scroll.Y - 3);
                Point end = new Point(
                    next.Left + next.Width / 2 + scroll.X,
                    next.Top + scroll.Y + 3);
                int bend = Math.Max(18, Math.Min(GridSize * 2, (end.Y - start.Y) / 2));
                using (System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    path.AddBezier(
                        start,
                        new Point(start.X, start.Y + bend),
                        new Point(end.X, end.Y - bend),
                        end);
                    e.Graphics.DrawPath(shadow, path);
                    e.Graphics.DrawPath(wire, path);
                }
            }
        }
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        e.Effect = e.Data.GetDataPresent(typeof(string)) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDragDrop(object sender, DragEventArgs e)
    {
        string definitionId = e.Data.GetData(typeof(string)) as string;
        BlockDefinition definition;
        if (definitionId == null || blockDefinitions == null ||
            !blockDefinitions.TryGetValue(definitionId, out definition))
        {
            return;
        }

        Point cursor = PointToClient(new Point(e.X, e.Y));
        Point logical = new Point(
            cursor.X - AutoScrollPosition.X,
            cursor.Y - AutoScrollPosition.Y);
        AddPlacedBlock(definition, FindFreePosition(Snap(logical), null));
        UpdateCanvasSize();
        Invalidate();
        RaiseDiagramChanged();
    }

    private void AddPlacedBlock(BlockDefinition definition, Point location)
    {
        CodeBlockControl block = new CodeBlockControl(definition, false);
        block.Size = new Size(CalculateBlockWidth(), BlockHeight);
        block.Location = Snap(location);
        block.MouseDown += OnBlockMouseDown;
        block.MouseMove += OnBlockMouseMove;
        block.MouseUp += OnBlockMouseUp;
        block.BlockHovered += ForwardBlockHovered;
        block.BlockHoverEnded += ForwardBlockHoverEnded;
        Controls.Add(block);
        block.BringToFront();
    }

    private void OnBlockMouseDown(object sender, MouseEventArgs e)
    {
        CodeBlockControl block = sender as CodeBlockControl;
        if (block == null || e.Button != MouseButtons.Left)
        {
            return;
        }

        if (e.X >= block.Width - 26 && e.Y <= 25)
        {
            Controls.Remove(block);
            block.Dispose();
            UpdateCanvasSize();
            Invalidate();
            RaiseDiagramChanged();
            return;
        }

        draggedBlock = block;
        dragOffset = e.Location;
        block.BringToFront();
    }

    private void OnBlockMouseMove(object sender, MouseEventArgs e)
    {
        CodeBlockControl block = sender as CodeBlockControl;
        if (block == null || draggedBlock != block || e.Button != MouseButtons.Left)
        {
            return;
        }

        Point cursor = PointToClient(block.PointToScreen(e.Location));
        Point logical = new Point(
            cursor.X - AutoScrollPosition.X - dragOffset.X,
            cursor.Y - AutoScrollPosition.Y - dragOffset.Y);
        block.Location = SnapAndClamp(logical, block);
        Invalidate();
    }

    private void OnBlockMouseUp(object sender, MouseEventArgs e)
    {
        CodeBlockControl block = sender as CodeBlockControl;
        if (block == null || draggedBlock != block || e.Button != MouseButtons.Left)
        {
            return;
        }

        draggedBlock = null;
        block.Location = FindFreePosition(block.Location, block);
        UpdateCanvasSize();
        Invalidate();
        RaiseDiagramChanged();
    }

    private void ForwardBlockHovered(object sender, BlockHelpEventArgs e)
    {
        EventHandler<BlockHelpEventArgs> handler = BlockHovered;
        if (handler != null)
        {
            handler(this, e);
        }
    }

    private void ForwardBlockHoverEnded(object sender, EventArgs e)
    {
        EventHandler handler = BlockHoverEnded;
        if (handler != null)
        {
            handler(this, EventArgs.Empty);
        }
    }

    private List<CodeBlockControl> OrderedBlocks()
    {
        return Controls
            .OfType<CodeBlockControl>()
            .Where(block => !block.IsPaletteItem)
            .OrderBy(block => block.Top)
            .ThenBy(block => block.Left)
            .ToList();
    }

    private int CalculateBlockWidth()
    {
        int usableWidth = Math.Max(GridSize * 8, ClientSize.Width - GridSize * 2);
        int snappedWidth = usableWidth / GridSize * GridSize;
        return Math.Max(GridSize * 8, Math.Min(MaximumBlockWidth, snappedWidth));
    }

    private Point Snap(Point point)
    {
        int width = CalculateBlockWidth();
        int maximumX = Math.Max(GridSize, ClientSize.Width - width - GridSize);
        int x = (int)Math.Round((double)point.X / GridSize) * GridSize;
        int y = (int)Math.Round((double)point.Y / GridSize) * GridSize;
        return new Point(Math.Max(GridSize, Math.Min(x, maximumX)), Math.Max(GridSize, y));
    }

    private Point SnapAndClamp(Point point, CodeBlockControl block)
    {
        Point snapped = Snap(point);
        return new Point(snapped.X, Math.Max(GridSize, snapped.Y));
    }

    private Point FindFreePosition(Point preferred, CodeBlockControl moving)
    {
        Point candidate = Snap(preferred);
        int attempts = Controls.Count + 2;
        while (attempts-- > 0)
        {
            Rectangle proposed = new Rectangle(candidate, new Size(CalculateBlockWidth(), BlockHeight));
            bool overlaps = false;
            foreach (CodeBlockControl block in OrderedBlocks())
            {
                if (block != moving &&
                    (Math.Abs(block.Top - candidate.Y) < BlockHeight + GridSize ||
                    proposed.IntersectsWith(block.Bounds)))
                {
                    overlaps = true;
                    break;
                }
            }

            if (!overlaps)
            {
                return candidate;
            }
            candidate.Y += GridSize;
        }

        return candidate;
    }

    private void UpdateBlockWidths()
    {
        int width = CalculateBlockWidth();
        foreach (CodeBlockControl block in OrderedBlocks())
        {
            block.Width = width;
            block.Location = Snap(new Point(block.Left, block.Top));
        }
        UpdateCanvasSize();
        Invalidate();
    }

    private void UpdateCanvasSize()
    {
        List<CodeBlockControl> blocks = OrderedBlocks();
        int bottom = blocks.Count == 0
            ? ClientSize.Height
            : blocks.Max(block => block.Bottom) + GridSize * 2;
        AutoScrollMinSize = new Size(CalculateBlockWidth() + GridSize * 2, Math.Max(ClientSize.Height, bottom));
    }

    private void RaiseDiagramChanged()
    {
        if (loadingBlocks)
        {
            return;
        }

        EventHandler handler = DiagramChanged;
        if (handler != null)
        {
            handler(this, EventArgs.Empty);
        }
    }
}
