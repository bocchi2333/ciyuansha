using System.IO;
using System.Linq;
using CiyuanSha.Gameplay.Generals;
using Godot;

namespace CiyuanSha.UI;

/// <summary>
/// Tool panel for mapping general ids to imported art files.
/// </summary>
public partial class GeneralArtMapperPanel : Control
{
    [Export]
    public NodePath GeneralOptionButtonPath { get; set; } = new NodePath();

    [Export]
    public NodePath ImageListPath { get; set; } = new NodePath();

    [Export]
    public NodePath PreviewTextureRectPath { get; set; } = new NodePath();

    [Export]
    public NodePath CurrentMappingLabelPath { get; set; } = new NodePath();

    [Export]
    public NodePath StatusLabelPath { get; set; } = new NodePath();

    [Export]
    public NodePath SaveButtonPath { get; set; } = new NodePath();

    [Export]
    public NodePath ReloadButtonPath { get; set; } = new NodePath();

    private OptionButton? _generalOptionButton;
    private ItemList? _imageList;
    private TextureRect? _previewTextureRect;
    private Label? _currentMappingLabel;
    private Label? _statusLabel;
    private Button? _saveButton;
    private Button? _reloadButton;

    public override void _Ready()
    {
        _generalOptionButton = GetNodeOrNull<OptionButton>(GeneralOptionButtonPath);
        _imageList = GetNodeOrNull<ItemList>(ImageListPath);
        _previewTextureRect = GetNodeOrNull<TextureRect>(PreviewTextureRectPath);
        _currentMappingLabel = GetNodeOrNull<Label>(CurrentMappingLabelPath);
        _statusLabel = GetNodeOrNull<Label>(StatusLabelPath);
        _saveButton = GetNodeOrNull<Button>(SaveButtonPath);
        _reloadButton = GetNodeOrNull<Button>(ReloadButtonPath);

        if (_generalOptionButton is not null)
        {
            _generalOptionButton.ItemSelected += HandleGeneralSelected;
        }

        if (_imageList is not null)
        {
            _imageList.ItemSelected += HandleImageSelected;
        }

        if (_saveButton is not null)
        {
            _saveButton.Pressed += SaveMapping;
        }

        if (_reloadButton is not null)
        {
            _reloadButton.Pressed += ReloadAll;
        }

        PopulateGenerals();
        PopulateImages();
        RefreshMappingState();
    }

    private void PopulateGenerals()
    {
        if (_generalOptionButton is null)
        {
            return;
        }

        _generalOptionButton.Clear();

        foreach (GeneralDefinition definition in GeneralCatalog.All.OrderBy(definition => definition.DisplayName))
        {
            _generalOptionButton.AddItem(definition.DisplayName);
            int index = _generalOptionButton.ItemCount - 1;
            _generalOptionButton.SetItemMetadata(index, definition.GeneralId);
        }
    }

    private void PopulateImages()
    {
        if (_imageList is null)
        {
            return;
        }

        _imageList.Clear();

        foreach (string fileName in GeneralArtRegistry.ListAvailableImageFiles())
        {
            _imageList.AddItem(fileName);
        }
    }

    private void HandleGeneralSelected(long index)
    {
        RefreshMappingState();
    }

    private void HandleImageSelected(long index)
    {
        RefreshPreview(GetSelectedImageFileName());
        RefreshMappingState();
    }

    private void SaveMapping()
    {
        string generalId = GetSelectedGeneralId();
        string fileName = GetSelectedImageFileName();

        if (string.IsNullOrWhiteSpace(generalId) || string.IsNullOrWhiteSpace(fileName))
        {
            SetStatus("Select both a general and an image file.");
            return;
        }

        GeneralArtRegistry.SetMapping(generalId, fileName);
        RefreshMappingState();
        SetStatus($"Saved mapping: {generalId} -> {fileName}");
    }

    private void ReloadAll()
    {
        GeneralArtRegistry.Reload();
        PopulateImages();
        RefreshMappingState();
        SetStatus($"Reloaded mapping file and image list from {CiyuanSha.Gameplay.Characters.PlayerCharacter.DefaultGeneralCardDirectory}.");
    }

    private void RefreshMappingState()
    {
        string generalId = GetSelectedGeneralId();
        GeneralDefinition definition = GeneralCatalog.Resolve(generalId, "Unknown");
        string explicitMapping = GeneralArtRegistry.GetExplicitMapping(generalId);
        string effectiveFile = GeneralArtRegistry.ResolveFileName(generalId, definition.CardFileName);

        if (_currentMappingLabel is not null)
        {
            _currentMappingLabel.Text = string.IsNullOrWhiteSpace(effectiveFile)
                ? "Current art: none"
                : string.IsNullOrWhiteSpace(explicitMapping)
                    ? $"Current art: {effectiveFile} (catalog default)"
                    : $"Current art: {effectiveFile} (override)";
        }

        SyncImageSelection(effectiveFile);

        if (!string.IsNullOrWhiteSpace(effectiveFile))
        {
            RefreshPreview(effectiveFile);
        }
        else
        {
            RefreshPreview(GetSelectedImageFileName());
        }
    }

    private void RefreshPreview(string fileName)
    {
        if (_previewTextureRect is null)
        {
            return;
        }

        _previewTextureRect.Texture = null;

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        string fullPath = Path.Combine(CiyuanSha.Gameplay.Characters.PlayerCharacter.DefaultGeneralCardDirectory, fileName);
        if (!File.Exists(fullPath))
        {
            SetStatus($"File not found: {fullPath}");
            return;
        }

        Image image = Image.LoadFromFile(fullPath);
        if (image.IsEmpty())
        {
            SetStatus($"Failed to load image: {fileName}");
            return;
        }

        _previewTextureRect.Texture = ImageTexture.CreateFromImage(image);
    }

    private string GetSelectedGeneralId()
    {
        if (_generalOptionButton is null || _generalOptionButton.ItemCount == 0)
        {
            return string.Empty;
        }

        return _generalOptionButton.GetItemMetadata(_generalOptionButton.Selected).AsString();
    }

    private string GetSelectedImageFileName()
    {
        if (_imageList is null || _imageList.GetSelectedItems().Length == 0)
        {
            return string.Empty;
        }

        return _imageList.GetItemText(_imageList.GetSelectedItems()[0]);
    }

    private void SyncImageSelection(string fileName)
    {
        if (_imageList is null || string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        for (int i = 0; i < _imageList.ItemCount; i++)
        {
            if (_imageList.GetItemText(i) != fileName)
            {
                continue;
            }

            _imageList.Select(i);
            _imageList.EnsureCurrentIsVisible();
            return;
        }
    }

    private void SetStatus(string text)
    {
        if (_statusLabel is not null)
        {
            _statusLabel.Text = text;
        }
    }
}
