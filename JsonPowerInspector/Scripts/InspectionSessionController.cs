﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text.Json.Nodes;
using Godot;
using GodotTask;
using JsonPowerInspector.Template;

namespace JsonPowerInspector;

public partial class InspectionSessionController : Control
{
    [Export] private Control _container;
    [Export] private Label _info;
    [Export] private Button _save;
    [Export] private Button _revert;
    
    public InspectorSpawner InspectorSpawner { get; private set; }
    private readonly List<IPropertyInspector> _inspectorRoot = [];
    public string TemplateDirectory { get; set; }
    public IReadOnlyDictionary<string, ObjectDefinition> ObjectDefinitionMap => _objectDefinitionMap;
    public bool Changed { get; private set; }

    private Dictionary<string, ObjectDefinition> _objectDefinitionMap;
    private ObjectDefinition _mainObjectDefinition;
    private JsonObject _editingJsonObject;
    private string _templatePath;
    private string _dataPath;

    public override void _Ready()
    {
        _save.Pressed += () => Save().Forget();
        _revert.Pressed += () =>
        {
            if (_dataPath == null) MountJsonObject(CreateTemplateJsonObject());
            else LoadFromJsonObject(_dataPath);
            Name = _mainObjectDefinition.ObjectTypeName;
            Changed = false;
        };
    }

    public void MarkChanged()
    {
        if(Changed) return;
        Changed = true;
        Name += " (*)";
    }

    public void StartSession(
        InspectorSpawner inspectorSpawner,
        string templatePath,
        string dataPath
    )
    {
        _templatePath = templatePath;

        PackedObjectDefinition setup;
        try
        {
            setup = TemplateSerializer.Deserialize(templatePath);
        }
        catch (Exception)
        {
            throw new SerializationException($"Error when reading the template file: {templatePath}");
        }
        
        _objectDefinitionMap = setup.ReferencedObjectDefinition.ToDictionary(x => x.ObjectTypeName, x => x);
        _mainObjectDefinition = setup.MainObjectDefinition;
        InspectorSpawner = inspectorSpawner;
        TemplateDirectory = Path.GetDirectoryName(templatePath)!;
        Name = setup.MainObjectDefinition.ObjectTypeName;
        
        if (dataPath != null)
        {
            LoadFromJsonObject(dataPath);
            return;
        }

        var templateJsonObject = CreateTemplateJsonObject();

        MountJsonObject(templateJsonObject);
    }

    private JsonObject CreateTemplateJsonObject()
    {
        JsonObject templateJsonObject = new();
        foreach (var propertyInfo in _mainObjectDefinition.Properties.AsSpan())
        {
            var propertyPath = new HashSet<BaseObjectPropertyInfo>();
            templateJsonObject.Add(propertyInfo.Name, Utils.CreateJsonObjectForProperty(propertyInfo, _objectDefinitionMap, propertyPath));
        }

        return templateJsonObject;
    }

    public void LoadFromJsonObject(string dataPath)
    {
        _dataPath = dataPath;
        JsonObject jsonObject;
        {
            using var fileStream = File.OpenRead(dataPath);
            jsonObject = (JsonObject)JsonObject.Parse(fileStream);
        }
        MountJsonObject(jsonObject);
    }

    private void MountJsonObject(JsonObject jsonObject) => MountJsonObjectImpl(jsonObject).Forget();
    
    private async GDTask MountJsonObjectImpl(JsonObject jsonObject)
    {
        if (Changed)
        {
            var discard = await Dialogs.OpenDataLossDialog();
            if(!discard) return;
        }
        
        foreach (var inspector in _inspectorRoot)
        {
            ((Control)inspector).QueueFree();
        }
        
        _inspectorRoot.Clear();
        
        foreach (var propertyInfo in _mainObjectDefinition.Properties)
        {
            var inspectorForProperty = Utils.CreateInspectorForProperty(propertyInfo, InspectorSpawner);
            _inspectorRoot.Add(inspectorForProperty);
            _container.AddChild((Control)inspectorForProperty);
        }        
        
        _editingJsonObject = jsonObject;
        var objectProperty = _editingJsonObject.ToArray();
        for (var index = 0; index < _inspectorRoot.Count; index++)
        {
            var jsonNode = objectProperty[index].Key;
            var propertyInspector = _inspectorRoot[index];
            propertyInspector.BindJsonNode(_editingJsonObject, jsonNode);
        }

        _info.Text =
            $"""
             Current Template:
             "{_templatePath}"
             Current Data:
             "{_dataPath ?? "New Data"}"
             """;
    }

    public async GDTask Save()
    {
        var jsonString = _editingJsonObject.ToJsonString(PowerTemplateJsonContext.Default.Options);
        if (_dataPath == null)
        {
            var selected = await Dialogs.OpenSaveFileDialog(TemplateDirectory);
            if (selected == null) return;
            _dataPath = selected;
        }
        File.WriteAllText(_dataPath, jsonString);
    }
}