using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VPP.Core.Engine;
using VPP.Core.Interfaces;
using VPP.Core.Models;
using VPP.Core.Services;
using VPP.Plugins.PointCloud;
using VPP.Plugins.PointCloud.Models;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using VPP.App.Rendering;
using Media3D = System.Windows.Media.Media3D;
using Color = System.Windows.Media.Color;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace VPP.App.ViewModels;

public partial class InspectionCardViewModel : ObservableObject
{
    [ObservableProperty] private string _nodeName = "";
    [ObservableProperty] private string _resultMessage = "";
    [ObservableProperty] private bool _isPass;
    [ObservableProperty] private string _details = "";
    [ObservableProperty] private DateTime _timestamp;
}

public partial class MainViewModel : ObservableObject
{
    private readonly PluginService _pluginService;
    private readonly ExecutionEngine _executionEngine;
    private VPP.Core.Models.ExecutionContext? _lastExecutionContext;

    [ObservableProperty] private NodeGraph _graph = new();
    [ObservableProperty] private ObservableCollection<NodeViewModel> _nodes = new();
    [ObservableProperty] private ObservableCollection<ConnectionViewModel> _connections = new();
    [ObservableProperty] private ObservableCollection<string> _availableNodes = new();
    [ObservableProperty] private string _selectedNodeType = "";
    [ObservableProperty] private NodeViewModel? _selectedNode;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private bool _isExecuting;

    // Node zoom scale for visual scaling only (doesn't affect canvas size)
    [ObservableProperty] private double _nodeZoomScale = 1.0;

    [ObservableProperty] private SceneViewModel _scene = new();

    // LOD settings
    private const int MAX_POINTS_FOR_FULL_RENDER = 10_000_000; // 10M points max
    private const int LOD_THRESHOLD_HIGH = 2_000_000; // 2M points - high quality
    private const int LOD_THRESHOLD_MEDIUM = 5_000_000; // 5M points - medium quality

    [ObservableProperty] private string _inspectionResult = "";
    [ObservableProperty] private bool _inspectionPass;
    [ObservableProperty] private double _detectedRadius;
    [ObservableProperty] private Media3D.Point3D _detectedCenter;
    [ObservableProperty] private bool _isResultPanelVisible;

    [ObservableProperty] private string _nodeSearchText = "";
    [ObservableProperty] private ObservableCollection<string> _filteredNodes = new();

    // ROI Drawing Mode
    [ObservableProperty] private bool _isRoiDrawingMode;
    [ObservableProperty] private NodeViewModel? _selectedRoiNode;

    // ROI Filter toggle (visible only when ROI Filter node is selected)
    [ObservableProperty] private bool _isRoiFilterToggleVisible;
    [ObservableProperty] private bool _isRoiFilterOn;
    private NodeViewModel? _selectedRoiFilterNode;

    // Circle Detection button visibility (visible only when Circle Detection node is selected)
    [ObservableProperty] private bool _isCircleDetectButtonVisible;
    private NodeViewModel? _selectedCircleDetectionNode;

    // Inspection button visibility (visible only when Inspection node is selected)
    [ObservableProperty] private bool _isInspectButtonVisible;
    private NodeViewModel? _selectedInspectionNode;
    
    // Track if we should force filter visualization for downstream nodes
    private bool _forceFilterVisualization = false;

    // Tab Navigation
    [ObservableProperty] private int _activeTabIndex = 0; // 0: Main, 1: Inspection
    [ObservableProperty] private ObservableCollection<InspectionCardViewModel> _inspectionCards = new();
    [ObservableProperty] private string _overallResultStatus = "READY"; // READY, PASS, FAIL
    [ObservableProperty] private bool _isOverallPass;

    // Execution time tracking
    [ObservableProperty] private string _totalExecutionTime = "";
    [ObservableProperty] private bool _isExecuteAllRunning;

    // Flag to skip intermediate UI updates during batch execution
    private bool _skipIntermediateUpdates = false;

    public MainViewModel()
    {
        _pluginService = new PluginService();
        _executionEngine = new ExecutionEngine();
        
        // Load built-in plugins
        _pluginService.LoadFromAssembly(typeof(PointCloudPlugin).Assembly);

        // Populate available nodes
        foreach (var (name, category, _) in _pluginService.GetAvailableNodes())
        {
            AvailableNodes.Add($"{category}/{name}");
        }

        _executionEngine.NodeExecuting += (s, e) =>
            StatusMessage = $"Executing: {e.Node.Name}";
        _executionEngine.NodeExecuted += (s, e) =>
        {
            StatusMessage = e.Result?.Success == true ? $"Completed: {e.Node.Name}" : $"Failed: {e.Node.Name}";

            // Skip intermediate UI updates during batch execution for performance
            if (_skipIntermediateUpdates) return;

            // Real-time visualization update after each node execution
            UpdateVisualization();
            // Update circle visualization if circle detection was executed
            if (e.Node.Name == "Circle Detection")
            {
                UpdateDetectedCircleVisualization();
            }
            // Update inspection cards if inspection was executed
            if (e.Node.Name == "Spec Inspection")
            {
                UpdateInspectionCards();
            }
        };
    }

    partial void OnIsRoiFilterOnChanged(bool value)
    {
        // When ROI Filter toggle changes, update visualization
        // This affects the selected node and all downstream nodes
        _forceFilterVisualization = value && _selectedRoiFilterNode != null;
        UpdateVisualization();
    }

    [RelayCommand]
    private void AddNode()
    {
        if (string.IsNullOrEmpty(SelectedNodeType)) return;

        var nodeName = SelectedNodeType.Split('/').Last();
        var node = _pluginService.CreateNode(nodeName);
        if (node == null) return;

        if (node is NodeBase nb)
        {
            nb.X = 100 + Nodes.Count * 200;
            nb.Y = 100;
        }

        Graph.AddNode(node);
        Nodes.Add(new NodeViewModel(node));
        StatusMessage = $"Added node: {node.Name}";
        UpdateInspectionCards();
    }

    public async Task ExecuteGraph()
    {
        if (IsExecuting) return;
        IsExecuting = true;
        StatusMessage = "Executing graph...";
        OverallResultStatus = "RUNNING...";

        try
        {
            var result = await _executionEngine.ExecuteAsync(Graph);

            if (result.Success)
            {
                _lastExecutionContext = result.Context;
                
                // Calculate and store ORIGINAL Z range from imported data (once)
                // This range will be used for depth visualization regardless of filtering
                CalculateAndSetOriginalDepthRange();
                
                StatusMessage = "Execution completed successfully";
                UpdateVisualization();
                UpdateDetectedCircleVisualization();
                UpdateInspectionCards();
            }
            else
            {
                var failedNode = result.NodeResults.FirstOrDefault(r => !r.Value.Success);
                StatusMessage = $"Execution failed: {failedNode.Value?.ErrorMessage}";
                OverallResultStatus = "FAIL";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            OverallResultStatus = "ERROR";
        }
        finally
        {
            IsExecuting = false;
        }
    }

    /// <summary>
    /// Calculate the global Z range from ALL imported point clouds and set it in SceneViewModel.
    /// This range will be used consistently for depth visualization.
    /// </summary>
    private void CalculateAndSetOriginalDepthRange()
    {
        if (_lastExecutionContext == null) return;

        float globalMinZ = float.MaxValue;
        float globalMaxZ = float.MinValue;
        bool foundAnyData = false;

        // Get all Import Point Cloud nodes
        var importNodes = Graph.Nodes.Where(n => n.Name == "Import Point Cloud").ToList();

        foreach (var importNode in importNodes)
        {
            var nodeCloud = _lastExecutionContext.Get<PointCloudData>($"{VPP.Core.Models.ExecutionContext.PointCloudKey}_{importNode.Id}");
            if (nodeCloud != null && nodeCloud.Points.Count > 0)
            {
                foundAnyData = true;
                foreach (var p in nodeCloud.Points)
                {
                    globalMinZ = Math.Min(globalMinZ, p.Z);
                    globalMaxZ = Math.Max(globalMaxZ, p.Z);
                }
            }
        }

        // If no import nodes found, try the global cloud (backward compatibility)
        if (!foundAnyData)
        {
            var globalCloud = _lastExecutionContext.Get<PointCloudData>(VPP.Core.Models.ExecutionContext.PointCloudKey);
            if (globalCloud != null && globalCloud.Points.Count > 0)
            {
                foundAnyData = true;
                foreach (var p in globalCloud.Points)
                {
                    globalMinZ = Math.Min(globalMinZ, p.Z);
                    globalMaxZ = Math.Max(globalMaxZ, p.Z);
                }
            }
        }

        // Set the original depth range in Scene
        if (foundAnyData)
        {
            Scene.SetOriginalDepthRange(globalMinZ, globalMaxZ);
        }
    }

    [RelayCommand]
    private async Task LoadPointCloud()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Point Cloud Files|*.ply;*.pcd;*.xyz;*.csv;*.txt|All Files|*.*",
            Title = "Select Point Cloud File"
        };

        if (dialog.ShowDialog() == true)
        {
            INode? targetNode = null;

            // Prioritize selected node if it is an import node
            if (SelectedNode != null && SelectedNode.Name == "Import Point Cloud")
            {
                targetNode = SelectedNode.Node;
            }
            else
            {
                // Fallback: find import nodes
                var importNodes = Graph.Nodes.Where(n => n.Name == "Import Point Cloud").ToList();
                
                if (importNodes.Count == 1)
                {
                    targetNode = importNodes[0];
                }
                else if (importNodes.Count > 1)
                {
                    StatusMessage = "Multiple Import nodes found. Please select the one you want to load into.";
                    return;
                }
            }

            if (targetNode != null)
            {
                var pathParam = targetNode.Parameters.FirstOrDefault(p => p.Name == "FilePath");
                if (pathParam != null)
                    pathParam.Value = dialog.FileName;
                
                StatusMessage = $"Loaded: {System.IO.Path.GetFileName(dialog.FileName)}";

                // Auto-execute to display the point cloud
                await ExecuteGraph();
            }
            else
            {
                StatusMessage = "Add 'Import Point Cloud' node first";
            }
        }
    }

    [RelayCommand]
    private async Task DetectCircle()
    {
        if (_selectedCircleDetectionNode == null)
        {
            StatusMessage = "No circle detection node selected";
            return;
        }

        // Auto-execute graph if not executed yet
        if (_lastExecutionContext == null)
        {
            StatusMessage = "Executing graph before detection...";
            await ExecuteGraph();
            if (_lastExecutionContext == null)
            {
                StatusMessage = "Failed to execute graph";
                return;
            }
        }

        try
        {
            // Get the CircleDetectionNode instance
            var circleNode = _selectedCircleDetectionNode.Node as VPP.Plugins.PointCloud.Nodes.CircleDetectionNode;
            if (circleNode == null)
            {
                StatusMessage = "Invalid circle detection node";
                return;
            }

            // Build detection cloud from connected ROI Filter
            var (detectionCloud, roiUsed) = BuildDetectionCloudForCircle(circleNode);
            if (detectionCloud == null || detectionCloud.Points.Count < 3)
            {
                StatusMessage = "Circle Detection: No suitable data (need >= 3 points). Ensure ROI Filter is connected and includes the hole boundary.";
                // Show full visualization
                UpdateVisualization();
                Scene.UpdateDetectedCircle(null, null);
                return;
            }

            StatusMessage = $"Detecting circle in {(roiUsed != null ? "ROI filtered" : "full")} data ({detectionCloud.Points.Count:N0} points)...";

            // Provide ROI to context so detector can choose plane accordingly
            // Note: We don't need to set ROIKey globally anymore as the node will look up connected ROI
            
            await Task.Run(() => circleNode.PerformDetection(_lastExecutionContext, detectionCloud, CancellationToken.None));

            UpdateVisualization();
            UpdateDetectedCircleVisualization();
            
            var result = _lastExecutionContext.Get<CircleDetectionResult>($"{VPP.Core.Models.ExecutionContext.CircleResultKey}_{circleNode.Id}");
            if (result != null && result.InlierCount > 0)
            {
                StatusMessage = $"✓ Circle detected! Radius: {result.Radius:F3}, Center: ({result.Center.X:F2}, {result.Center.Y:F2}, {result.Center.Z:F2}), Inliers: {result.InlierCount}/{detectionCloud.Points.Count}";
            }
            else
            {
                StatusMessage = "Circle detection failed - adjust ROI and parameters (DistanceThreshold, Min/MaxRadius).";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Detection error: {ex.Message}";
        }
    }

    private (PointCloudData? cloud, ROI3D? roiUsed) BuildDetectionCloudForCircle(VPP.Plugins.PointCloud.Nodes.CircleDetectionNode? circleNode = null)
    {
        if (_lastExecutionContext == null) return (null, null);

        // If a specific circle node is provided, try to get its connected filtered cloud directly
        if (circleNode != null)
        {
            // We need to find the connected ROI Filter ID
            var roiFilterNodeId = Graph.Connections
                .Where(c => c.TargetNodeId == circleNode.Id)
                .Select(c => c.SourceNodeId)
                .FirstOrDefault(id => 
                {
                    var node = Graph.Nodes.FirstOrDefault(n => n.Id == id);
                    return node?.Name == "ROI Filter";
                });

            if (roiFilterNodeId != null)
            {
                var filteredCloud = _lastExecutionContext.Get<PointCloudData>($"{VPP.Core.Models.ExecutionContext.FilteredCloudKey}_{roiFilterNodeId}");
                var connectedRoi = _lastExecutionContext.Get<ROI3D>($"{VPP.Core.Models.ExecutionContext.ROIKey}_{roiFilterNodeId}");
                
                if (filteredCloud != null && filteredCloud.Points.Count > 0)
                {
                    return (filteredCloud, connectedRoi);
                }
            }
        }

        // Fallback logic (should rarely be reached if graph is connected properly)
        // Reuse visualization pipeline to gather all clouds
        var clouds = new List<PointCloudData>();
        var importNodes = Graph.Nodes.Where(n => n.Name == "Import Point Cloud").ToList();
        var transformNodes = Graph.Nodes.Where(n => n.Name == "Rigid Transform").ToList();

        var transformIdSet = new HashSet<string>(transformNodes.Select(t => t.Id));
        var nonLeafTransformIds = new HashSet<string>(
            Graph.Connections
                 .Where(c => transformIdSet.Contains(c.SourceNodeId) && transformIdSet.Contains(c.TargetNodeId))
                 .Select(c => c.SourceNodeId)
        );
        var leafTransforms = transformNodes.Where(t => !nonLeafTransformIds.Contains(t.Id)).ToList();

        var successfullyTransformedImportIds = new HashSet<string>();
        foreach (var t in leafTransforms)
        {
            var transformedCloud = _lastExecutionContext.Get<PointCloudData>($"TransformedCloud_{t.Id}");
            if (transformedCloud != null && transformedCloud.Points.Count > 0)
            {
                clouds.Add(transformedCloud);
                foreach (var upId in GetUpstreamImportIds(t.Id))
                    successfullyTransformedImportIds.Add(upId);
            }
        }
        foreach (var importNode in importNodes)
        {
            if (!successfullyTransformedImportIds.Contains(importNode.Id))
            {
                var nodeCloud = _lastExecutionContext.Get<PointCloudData>($"{VPP.Core.Models.ExecutionContext.PointCloudKey}_{importNode.Id}");
                if (nodeCloud != null && nodeCloud.Points.Count > 0)
                {
                    clouds.Add(nodeCloud);
                }
            }
        }
        if (clouds.Count == 0)
        {
            var globalCloud = _lastExecutionContext.Get<PointCloudData>(VPP.Core.Models.ExecutionContext.PointCloudKey);
            if (globalCloud != null && globalCloud.Points.Count > 0)
                clouds.Add(globalCloud);
        }

        // Determine ROI from connected ROI Filter
        ROI3D? roi = null;
        
        // Use the ROI Filter that was identified when the node was selected
        if (_selectedRoiFilterNode != null)
        {
            roi = BuildRoiFromConnectedDrawNode(_selectedRoiFilterNode);
        }
        // Fallback: try to find connected ROI Filter if not already set
        else if (_selectedCircleDetectionNode != null)
        {
            var roiFilterNode = Graph.Connections
                .Where(c => c.TargetNodeId == _selectedCircleDetectionNode.Node.Id)
                .Select(c => Graph.Nodes.FirstOrDefault(n => n.Id == c.SourceNodeId && n.Name == "ROI Filter"))
                .FirstOrDefault();

            if (roiFilterNode != null)
            {
                var roiFilterNodeVm = Nodes.FirstOrDefault(n => n.Node.Id == roiFilterNode.Id);
                if (roiFilterNodeVm != null)
                {
                    roi = BuildRoiFromConnectedDrawNode(roiFilterNodeVm);
                }
            }
        }

        // Apply ROI across all clouds if available
        var cloudsToUse = roi != null ? clouds.Select(c => FilterCloudByRoi(c, roi)).ToList() : clouds;

        // Merge to single cloud for detection
        var merged = new PointCloudData();
        foreach (var c in cloudsToUse)
        {
            if (c == null || c.Points.Count == 0) continue;
            merged.Points.AddRange(c.Points);
            if (c.Colors != null)
            {
                merged.Colors ??= new List<System.Numerics.Vector3>();
                merged.Colors.AddRange(c.Colors);
            }
            if (c.Normals != null)
            {
                merged.Normals ??= new List<System.Numerics.Vector3>();
                merged.Normals.AddRange(c.Normals);
            }
        }
        merged.ComputeBoundingBox();
        return (merged.Points.Count > 0 ? merged : null, roi);
    }

    [RelayCommand]
    private async Task Inspect()
    {
        if (_selectedInspectionNode == null)
        {
            StatusMessage = "No inspection node selected";
            return;
        }

        // Auto-execute graph if not executed yet
        if (_lastExecutionContext == null)
        {
            StatusMessage = "Executing graph before inspection...";
            await ExecuteGraph();
            if (_lastExecutionContext == null)
            {
                StatusMessage = "Failed to execute graph";
                return;
            }
        }

        try
        {
            StatusMessage = "Performing inspection...";

            // Get the InspectionNode instance
            var inspectionNode = _selectedInspectionNode.Node as VPP.Plugins.PointCloud.Nodes.InspectionNode;
            if (inspectionNode == null)
            {
                StatusMessage = "Invalid inspection node";
                return;
            }

            // Perform inspection
            await Task.Run(() => inspectionNode.PerformInspection(_lastExecutionContext));

            // Update visualization to show inspection result
            UpdateInspectionCards();

            var result = _lastExecutionContext.Get<InspectionResult>($"InspectionResult_{inspectionNode.Id}");
            if (result != null)
            {
                StatusMessage = result.Pass
                    ? $"✓ INSPECTION PASSED - {result.Message}"
                    : $"✗ INSPECTION FAILED - {result.Message}";
                InspectionPass = result.Pass;
                InspectionResult = result.Message;
            }
            else
            {
                StatusMessage = "Inspection failed - no result generated";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Inspection error: {ex.Message}";
        }
    }

    private void UpdateInspectionCards()
    {
        var cards = new List<InspectionCardViewModel>();
        bool allPass = true;
        bool anyInspectionFound = false;

        // Find all inspection nodes
        var inspectionNodes = Graph.Nodes.Where(n => n.Name == "Spec Inspection").ToList();

        foreach (var node in inspectionNodes)
        {
            anyInspectionFound = true;
            
            InspectionResult? result = null;
            if (_lastExecutionContext != null)
            {
                result = _lastExecutionContext.Get<InspectionResult>($"InspectionResult_{node.Id}");
            }
            
            var card = new InspectionCardViewModel
            {
                NodeName = node.Name,
                Timestamp = DateTime.Now
            };

            if (result != null)
            {
                card.IsPass = result.Pass;
                card.ResultMessage = result.Message;
                
                if (result.Measurements != null && result.Measurements.Count > 0)
                {
                    var details = string.Join("\n", result.Measurements.Select(m => $"{m.Key}: {m.Value:F3}"));
                    card.Details = details;
                }
                else
                {
                    card.Details = result.Pass ? "Within specifications" : "Out of specifications";
                }

                if (!result.Pass) allPass = false;
            }
            else
            {
                card.IsPass = false;
                card.ResultMessage = "Not Executed";
                card.Details = "No result available";
                allPass = false;
            }

            cards.Add(card);
        }

        InspectionCards = new ObservableCollection<InspectionCardViewModel>(cards);

        if (!anyInspectionFound)
        {
            OverallResultStatus = "READY";
            IsOverallPass = false;
        }
        else
        {
            // Check if any results actually exist (not just "Not Executed")
            bool anyResultExists = cards.Any(c => c.ResultMessage != "Not Executed");
            
            if (!anyResultExists)
            {
                 OverallResultStatus = "READY";
                 IsOverallPass = false;
            }
            else
            {
                IsOverallPass = allPass;
                OverallResultStatus = allPass ? "PASS" : "FAIL";
            }
        }
    }

    private void UpdateDetectedCircleVisualization()
    {
        if (_lastExecutionContext == null) 
        {
            Scene.UpdateDetectedCircles(Enumerable.Empty<DetectedCircleData>());
            return;
        }

        var circleDataList = new List<DetectedCircleData>();

        // Filter visible circles based on selection
        if (_selectedCircleDetectionNode != null)
        {
            // Show only the selected circle detection node's result
            AddCircleResultToList(circleDataList, _selectedCircleDetectionNode.Node.Id);
        }
        else if (_selectedInspectionNode != null)
        {
            // Show the circle detection result connected to the selected inspection node
            var circleNode = Graph.Connections
                .Where(c => c.TargetNodeId == _selectedInspectionNode.Node.Id)
                .Select(c => Graph.Nodes.FirstOrDefault(n => n.Id == c.SourceNodeId && n.Name == "Circle Detection"))
                .FirstOrDefault();
            
            if (circleNode != null)
            {
                AddCircleResultToList(circleDataList, circleNode.Id);
            }
        }
        else
        {
            // Show all detected circles if no specific node is selected (Overview)
            var circleNodes = Graph.Nodes.Where(n => n.Name == "Circle Detection").ToList();
            foreach (var node in circleNodes)
            {
                AddCircleResultToList(circleDataList, node.Id);
            }
        }

        // Fallback for legacy/global result if list is still empty
        if (circleDataList.Count == 0)
        {
             var detectedCloud = _lastExecutionContext.Get<PointCloudData>("DetectedCircleCloud");
             var circleResult = _lastExecutionContext.Get<CircleDetectionResult>(VPP.Core.Models.ExecutionContext.CircleResultKey);
             
             if (detectedCloud != null || circleResult != null)
             {
                 circleDataList.Add(new DetectedCircleData
                 {
                     NodeId = "Global",
                     Cloud = detectedCloud,
                     Result = circleResult
                 });
             }
        }

        Scene.UpdateDetectedCircles(circleDataList);

        // Update status message
        if (circleDataList.Count > 0)
        {
            var count = circleDataList.Count(c => c.Result != null && c.Result.Radius > 0);
            if (count > 0 && !StatusMessage.Contains("Circle(s) Detected"))
            {
                StatusMessage += $" | {count} Circle(s) Detected";
            }
        }
    }

    private void AddCircleResultToList(List<DetectedCircleData> list, string nodeId)
    {
        var detectedCloud = _lastExecutionContext?.Get<PointCloudData>($"DetectedCircleCloud_{nodeId}");
        var circleResult = _lastExecutionContext?.Get<CircleDetectionResult>($"{VPP.Core.Models.ExecutionContext.CircleResultKey}_{nodeId}");

        if (detectedCloud != null || circleResult != null)
        {
            list.Add(new DetectedCircleData
            {
                NodeId = nodeId,
                Cloud = detectedCloud,
                Result = circleResult
            });
        }
    }

    [RelayCommand]
    private void ClearWorkflow()
    {
        // Clear Graph and UI collections
        Graph = new NodeGraph();
        Nodes.Clear();
        Connections.Clear();

        // Reset execution context
        _lastExecutionContext = null;

        // Reset selection and state
        SelectNode(null);

        // Clear Scene (also clears depth range)
        Scene.ClearPointCloud();
        
        // Reset Inspection/Detection results
        InspectionPass = false;
        InspectionResult = "";
        DetectedRadius = 0;
        DetectedCenter = new Media3D.Point3D(0, 0, 0);

        UpdateInspectionCards();
        StatusMessage = "Workflow cleared";
    }

    [RelayCommand]
    private void CreateDefaultWorkflow()
    {
        // Clear existing
        Graph = new NodeGraph();
        Nodes.Clear();
        Connections.Clear();

        // Create workflow: Import -> ROI Draw & ROI Filter -> Circle Detection -> Inspection
        var importNode = _pluginService.CreateNode("Import Point Cloud") as NodeBase;
        var roiDrawNode = _pluginService.CreateNode("ROI Draw") as NodeBase;
        var roiFilterNode = _pluginService.CreateNode("ROI Filter") as NodeBase;
        var circleNode = _pluginService.CreateNode("Circle Detection") as NodeBase;
        var inspectNode = _pluginService.CreateNode("Spec Inspection") as NodeBase;

        if (importNode == null || roiDrawNode == null || roiFilterNode == null ||
            circleNode == null || inspectNode == null) return;

        // Position nodes
        importNode.X = 50; importNode.Y = 150;
        roiDrawNode.X = 50; roiDrawNode.Y = 350;
        roiFilterNode.X = 300; roiFilterNode.Y = 200;
        circleNode.X = 550; circleNode.Y = 200;
        inspectNode.X = 800; inspectNode.Y = 200;

        // Set default parameter values
        roiDrawNode.Parameters.First(p => p.Name == "SizeX").Value = 100f;
        roiDrawNode.Parameters.First(p => p.Name == "SizeY").Value = 100f;
        roiDrawNode.Parameters.First(p => p.Name == "SizeZ").Value = 50f;

        inspectNode.Parameters.First(p => p.Name == "RadiusMin").Value = 5f;
        inspectNode.Parameters.First(p => p.Name == "RadiusMax").Value = 15f;

        // Add to graph
        Graph.AddNode(importNode);
        Graph.AddNode(roiDrawNode);
        Graph.AddNode(roiFilterNode);
        Graph.AddNode(circleNode);
        Graph.AddNode(inspectNode);

        // Connect nodes - Only connect Import -> ROI Filter -> Circle Detection
        Graph.Connect(importNode, roiDrawNode);      // Import provides data to ROI Draw
        Graph.Connect(importNode, roiFilterNode);    // Import provides data to ROI Filter
        Graph.Connect(roiDrawNode, roiFilterNode);   // ROI Draw defines the ROI region
        Graph.Connect(roiFilterNode, circleNode);    // ROI Filter provides FILTERED data to Circle Detection
        Graph.Connect(circleNode, inspectNode);      // Circle Detection provides result to Inspection

        // Create view models
        var importNodeVm = new NodeViewModel(importNode);
        var roiDrawNodeVm = new NodeViewModel(roiDrawNode);
        var roiFilterNodeVm = new NodeViewModel(roiFilterNode);
        var circleNodeVm = new NodeViewModel(circleNode);
        var inspectNodeVm = new NodeViewModel(inspectNode);

        Nodes.Add(importNodeVm);
        Nodes.Add(roiDrawNodeVm);
        Nodes.Add(roiFilterNodeVm);
        Nodes.Add(circleNodeVm);
        Nodes.Add(inspectNodeVm);

        // Create connection view models with proper references
        var nodeVmMap = Nodes.ToDictionary(n => n.Node.Id, n => n);
        foreach (var conn in Graph.Connections)
        {
            var sourceVm = nodeVmMap[conn.SourceNodeId];
            var targetVm = nodeVmMap[conn.TargetNodeId];
            Connections.Add(new ConnectionViewModel(conn, sourceVm, targetVm));
        }

        UpdateInspectionCards();
        StatusMessage = "Created default workflow - Circle Detection will use ROI filtered data only";
    }

    [RelayCommand]
    private void SetActiveTab(string indexStr)
    {
        if (int.TryParse(indexStr, out int index))
        {
            ActiveTabIndex = index;
        }
    }

    [RelayCommand]
    private async Task ExecuteAll()
    {
        if (IsExecuteAllRunning) return;
        IsExecuteAllRunning = true;
        TotalExecutionTime = "";
        OverallResultStatus = "RUNNING...";

        // Skip intermediate UI updates for faster execution
        _skipIntermediateUpdates = true;

        var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Step 1: Execute the graph to load point clouds and apply filters
            StatusMessage = "Executing graph...";
            await ExecuteGraph();

            if (_lastExecutionContext == null)
            {
                StatusMessage = "Execution failed - no context available";
                OverallResultStatus = "ERROR";
                return;
            }

            // Step 2: Find all Circle Detection nodes and run detection in parallel
            var circleDetectionNodes = Graph.Nodes
                .Where(n => n.Name == "Circle Detection")
                .ToList();

            StatusMessage = $"Detecting circles ({circleDetectionNodes.Count} nodes)...";

            // Prepare all detection tasks
            var detectionTasks = new List<Task>();
            foreach (var circleNodeModel in circleDetectionNodes)
            {
                var circleNode = circleNodeModel as VPP.Plugins.PointCloud.Nodes.CircleDetectionNode;
                if (circleNode == null) continue;

                var (detectionCloud, _) = BuildDetectionCloudForCircleNode(circleNode);
                if (detectionCloud != null && detectionCloud.Points.Count >= 3)
                {
                    var cloud = detectionCloud; // Capture for closure
                    var node = circleNode;
                    detectionTasks.Add(Task.Run(() => node.PerformDetection(_lastExecutionContext, cloud, CancellationToken.None)));
                }
            }

            // Wait for all detections to complete
            if (detectionTasks.Count > 0)
            {
                await Task.WhenAll(detectionTasks);
            }

            // Step 3: Find all Inspection nodes and run inspection in parallel
            var inspectionNodes = Graph.Nodes
                .Where(n => n.Name == "Spec Inspection")
                .ToList();

            StatusMessage = $"Performing inspections ({inspectionNodes.Count} nodes)...";

            // Prepare all inspection tasks
            var inspectionTasks = new List<Task>();
            foreach (var inspectionNodeModel in inspectionNodes)
            {
                var inspectionNode = inspectionNodeModel as VPP.Plugins.PointCloud.Nodes.InspectionNode;
                if (inspectionNode == null) continue;

                var node = inspectionNode;
                inspectionTasks.Add(Task.Run(() => node.PerformInspection(_lastExecutionContext)));
            }

            // Wait for all inspections to complete
            if (inspectionTasks.Count > 0)
            {
                await Task.WhenAll(inspectionTasks);
            }

            // Step 4: Update all visualizations (once at the end)
            totalStopwatch.Stop();
            TotalExecutionTime = FormatExecutionTime(totalStopwatch.Elapsed);

            UpdateVisualization();
            UpdateDetectedCircleVisualization();
            UpdateInspectionCards();

            StatusMessage = $"Execute All completed - {circleDetectionNodes.Count} detections, {inspectionNodes.Count} inspections | Total: {TotalExecutionTime}";
        }
        catch (Exception ex)
        {
            totalStopwatch.Stop();
            TotalExecutionTime = FormatExecutionTime(totalStopwatch.Elapsed);
            StatusMessage = $"Execute All failed: {ex.Message}";
            OverallResultStatus = "ERROR";
        }
        finally
        {
            _skipIntermediateUpdates = false;
            IsExecuteAllRunning = false;
        }
    }

    private (PointCloudData? cloud, ROI3D? roiUsed) BuildDetectionCloudForCircleNode(VPP.Plugins.PointCloud.Nodes.CircleDetectionNode circleNode)
    {
        if (_lastExecutionContext == null) return (null, null);

        // Find the connected ROI Filter ID
        var roiFilterNodeId = Graph.Connections
            .Where(c => c.TargetNodeId == circleNode.Id)
            .Select(c => c.SourceNodeId)
            .FirstOrDefault(id =>
            {
                var node = Graph.Nodes.FirstOrDefault(n => n.Id == id);
                return node?.Name == "ROI Filter";
            });

        if (roiFilterNodeId != null)
        {
            var filteredCloud = _lastExecutionContext.Get<PointCloudData>($"{VPP.Core.Models.ExecutionContext.FilteredCloudKey}_{roiFilterNodeId}");
            var connectedRoi = _lastExecutionContext.Get<ROI3D>($"{VPP.Core.Models.ExecutionContext.ROIKey}_{roiFilterNodeId}");

            if (filteredCloud != null && filteredCloud.Points.Count > 0)
            {
                return (filteredCloud, connectedRoi);
            }
        }

        // Fallback: use global cloud
        var globalCloud = _lastExecutionContext.Get<PointCloudData>(VPP.Core.Models.ExecutionContext.PointCloudKey);
        return (globalCloud, null);
    }

    private string FormatExecutionTime(TimeSpan elapsed)
    {
        if (elapsed.TotalMilliseconds < 1000)
        {
            return $"{elapsed.TotalMilliseconds:F0}ms";
        }
        else if (elapsed.TotalSeconds < 60)
        {
            return $"{elapsed.TotalSeconds:F2}s";
        }
        else
        {
            return $"{elapsed.Minutes}m {elapsed.Seconds:D2}s";
        }
    }

    private void UpdateVisualization()
    {
        if (_lastExecutionContext == null) return;

        int totalOriginalPoints = 0;
        var clouds = new List<PointCloudData>();

        // If an Import Point Cloud node is selected, show only its data
        if (SelectedNode != null && SelectedNode.Name == "Import Point Cloud")
        {
            var nodeCloud = _lastExecutionContext.Get<PointCloudData>($"{VPP.Core.Models.ExecutionContext.PointCloudKey}_{SelectedNode.Node.Id}");
            if (nodeCloud != null && nodeCloud.Points.Count > 0)
            {
                clouds.Add(nodeCloud);
                totalOriginalPoints = nodeCloud.Points.Count;
            }
        }
        else
        {
            // All Import and Transform nodes
            var importNodes = Graph.Nodes.Where(n => n.Name == "Import Point Cloud").ToList();
            var transformNodes = Graph.Nodes.Where(n => n.Name == "Rigid Transform").ToList();

            // Determine transform chain leaves (transforms that do not feed into another transform)
            var transformIdSet = new HashSet<string>(transformNodes.Select(t => t.Id));
            var nonLeafTransformIds = new HashSet<string>(
                Graph.Connections
                     .Where(c => transformIdSet.Contains(c.SourceNodeId) && transformIdSet.Contains(c.TargetNodeId))
                     .Select(c => c.SourceNodeId)
            );
            var leafTransforms = transformNodes.Where(t => !nonLeafTransformIds.Contains(t.Id)).ToList();

            // Track which imports have been successfully transformed
            var successfullyTransformedImportIds = new HashSet<string>();

            // Add leaf transformed clouds (and track their source imports)
            foreach (var t in leafTransforms)
            {
                var transformedCloud = _lastExecutionContext.Get<PointCloudData>($"TransformedCloud_{t.Id}");
                if (transformedCloud != null && transformedCloud.Points.Count > 0)
                {
                    clouds.Add(transformedCloud);
                    totalOriginalPoints += transformedCloud.Points.Count;

                    // Mark the upstream imports as successfully transformed
                    foreach (var upId in GetUpstreamImportIds(t.Id))
                        successfullyTransformedImportIds.Add(upId);
                }
            }

            // Add import clouds that either:
            // 1. Have no transform downstream, OR
            // 2. Have a transform but it failed (no TransformedCloud exists)
            foreach (var importNode in importNodes)
            {
                if (!successfullyTransformedImportIds.Contains(importNode.Id))
                {
                    var nodeCloud = _lastExecutionContext.Get<PointCloudData>($"{VPP.Core.Models.ExecutionContext.PointCloudKey}_{importNode.Id}");
                    if (nodeCloud != null && nodeCloud.Points.Count > 0)
                    {
                        clouds.Add(nodeCloud);
                        totalOriginalPoints += nodeCloud.Points.Count;
                    }
                }
            }

            // Fallback: only raw global cloud
            if (clouds.Count == 0)
            {
                var globalCloud = _lastExecutionContext.Get<PointCloudData>(VPP.Core.Models.ExecutionContext.PointCloudKey);
                if (globalCloud != null && globalCloud.Points.Count > 0)
                {
                    clouds.Add(globalCloud);
                    totalOriginalPoints = globalCloud.Points.Count;
                }
            }
        }

        // Apply ROI filter if:
        // 1. ROI Filter node is selected and toggle is ON
        // 2. OR Circle Detection node is selected (auto-filter)
        // 3. OR any downstream node of ROI Filter is selected and filter is ON
        bool shouldApplyFilter = false;
        ROI3D? roi = null;

        if (_selectedRoiFilterNode != null && IsRoiFilterOn)
        {
            // ROI Filter node is selected with filter ON
            shouldApplyFilter = true;
            roi = BuildRoiFromConnectedDrawNode(_selectedRoiFilterNode);
        }
        else if (_selectedCircleDetectionNode != null || _forceFilterVisualization)
        {
            // Circle Detection or other downstream node selected
            // Use the ROI Filter connected to the selected node (if any)
            if (_selectedRoiFilterNode != null)
            {
                shouldApplyFilter = true;
                roi = BuildRoiFromConnectedDrawNode(_selectedRoiFilterNode);
            }
        }

        if (shouldApplyFilter && roi != null)
        {
            var filteredClouds = new List<PointCloudData>(clouds.Count);
            foreach (var cloud in clouds)
                filteredClouds.Add(FilterCloudByRoi(cloud, roi));
            clouds = filteredClouds;
            totalOriginalPoints = clouds.Sum(c => c.Points.Count);
        }

        if (clouds.Count == 0)
        {
            StatusMessage = "No points to display";
            Scene.ClearPointCloud();
            return;
        }

        try
        {
            Scene.UpdatePointCloud(clouds, fitCamera: true);
            
            // Calculate stats for status message
            int renderedPoints = Scene.PointCloudGeometry?.Positions?.Count ?? 0;
            var memoryMb = GpuPointCloudRenderer.EstimateMemoryUsage(renderedPoints, Scene.PointCloudGeometry?.Colors != null) / (1024.0 * 1024.0);
            var filterStatus = shouldApplyFilter ? " [ROI FILTERED]" : "";
            StatusMessage = $"Displaying {renderedPoints:N0}/{totalOriginalPoints:N0} points ({clouds.Count} cloud(s)){filterStatus} | GPU Memory: ~{memoryMb:F1} MB";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error rendering point clouds: {ex.Message}";
            return;
        }

        UpdateInspectionResults();
    }

    // Collect upstream Import Point Cloud node ID's for a given node id
    private IEnumerable<string> GetUpstreamImportIds(string nodeId)
    {
        var result = new HashSet<string>();
        var visited = new HashSet<string>();
        void Dfs(string targetId)
        {
            if (!visited.Add(targetId)) return;
            foreach (var conn in Graph.Connections.Where(c => c.TargetNodeId == targetId))
            {
                var source = Graph.Nodes.FirstOrDefault(n => n.Id == conn.SourceNodeId);
                if (source == null) continue;
                if (source.Name == "Import Point Cloud")
                {
                    result.Add(source.Id);
                }
                else
                {
                    Dfs(source.Id);
                }
            }
        }
        Dfs(nodeId);
        return result;
    }

    private ROI3D? BuildRoiFromConnectedDrawNode(NodeViewModel roiFilterNodeVm)
    {
        // Find an incoming connection from a ROI Draw node
        var incoming = Graph.Connections.Where(c => c.TargetNodeId == roiFilterNodeVm.Node.Id)
                                        .Select(c => Graph.Nodes.FirstOrDefault(n => n.Id == c.SourceNodeId))
                                        .FirstOrDefault(n => n != null && n.Name == "ROI Draw");
        if (incoming == null) return null;

        string shapeStr = GetParameterValue<string>(incoming, "Shape") ?? "Box";
        var shape = shapeStr.ToLower() switch
        {
            "cylinder" => ROIShape.Cylinder,
            "sphere" => ROIShape.Sphere,
            _ => ROIShape.Box
        };

        var center = new System.Numerics.Vector3(
            GetParameterValue<float>(incoming, "CenterX"),
            GetParameterValue<float>(incoming, "CenterY"),
            GetParameterValue<float>(incoming, "CenterZ"));

        var size = new System.Numerics.Vector3(
            GetParameterValue<float>(incoming, "SizeX"),
            GetParameterValue<float>(incoming, "SizeY"),
            GetParameterValue<float>(incoming, "SizeZ"));

        var radius = GetParameterValue<float>(incoming, "Radius");

        return new ROI3D
        {
            Center = center,
            Size = size,
            Radius = radius,
            Shape = shape
        };
    }

    private PointCloudData FilterCloudByRoi(PointCloudData cloud, ROI3D roi)
    {
        var result = new PointCloudData();
        for (int i = 0; i < cloud.Points.Count; i++)
        {
            var p = cloud.Points[i];
            if (IsInROI(p, roi))
            {
                result.Points.Add(p);
                if (cloud.Colors != null && i < cloud.Colors.Count)
                {
                    result.Colors ??= new List<System.Numerics.Vector3>();
                    result.Colors.Add(cloud.Colors[i]);
                }
                if (cloud.Normals != null && i < cloud.Normals.Count)
                {
                    result.Normals ??= new List<System.Numerics.Vector3>();
                    result.Normals.Add(cloud.Normals[i]);
                }
            }
        }
        result.ComputeBoundingBox();
        return result;
    }

    private bool IsInROI(System.Numerics.Vector3 point, ROI3D roi)
    {
        var diff = point - roi.Center;
        return roi.Shape switch
        {
            ROIShape.Box =>
                Math.Abs(diff.X) <= roi.Size.X / 2 &&
                Math.Abs(diff.Y) <= roi.Size.Y / 2 &&
                Math.Abs(diff.Z) <= roi.Size.Z / 2,
            ROIShape.Cylinder =>
                Math.Sqrt(diff.X * diff.X + diff.Z * diff.Z) <= roi.Radius &&
                Math.Abs(diff.Y) <= roi.Size.Y / 2,
            ROIShape.Sphere =>
                diff.Length() <= roi.Radius,
            _ => false
        };
    }

    private void UpdateInspectionResults()
    {
        if (_lastExecutionContext == null) return;

        // Reset values to ensure they are hidden when not selected
        InspectionPass = false;
        InspectionResult = "";
        DetectedRadius = 0;
        DetectedCenter = new Media3D.Point3D(0, 0, 0);

        // If inspection node selected, show its specific result
        if (_selectedInspectionNode != null)
        {
            if (_lastExecutionContext.TryGet<InspectionResult>($"InspectionResult_{_selectedInspectionNode.Node.Id}", out var inspectionResult))
            {
                InspectionPass = inspectionResult.Pass;
                InspectionResult = inspectionResult.Message;

                if (inspectionResult.Measurements != null && inspectionResult.Measurements.TryGetValue("Radius", out var radius))
                {
                    DetectedRadius = radius;
                }
            }

            // If Radius is still 0, try to get it from the connected Circle Detection node
            if (DetectedRadius == 0)
            {
                var sourceNodeId = Graph.Connections
                    .Where(c => c.TargetNodeId == _selectedInspectionNode.Node.Id)
                    .Select(c => c.SourceNodeId)
                    .FirstOrDefault();
                
                if (sourceNodeId != null)
                {
                    var circleResult = _lastExecutionContext.Get<CircleDetectionResult>($"{VPP.Core.Models.ExecutionContext.CircleResultKey}_{sourceNodeId}");
                    if (circleResult != null)
                    {
                        DetectedRadius = circleResult.Radius;
                        DetectedCenter = new Media3D.Point3D(circleResult.Center.X, circleResult.Center.Y, circleResult.Center.Z);
                    }
                }
            }
        }

        // If circle node selected, show its specific result
        if (_selectedCircleDetectionNode != null)
        {
            var circle = _lastExecutionContext.Get<CircleDetectionResult>($"{VPP.Core.Models.ExecutionContext.CircleResultKey}_{_selectedCircleDetectionNode.Node.Id}");
            if (circle != null)
            {
                DetectedRadius = circle.Radius;
                DetectedCenter = new Media3D.Point3D(circle.Center.X, circle.Center.Y, circle.Center.Z);
            }
        }
    }

    public void UpdateFilteredNodes()
    {
        FilteredNodes.Clear();
        var search = NodeSearchText?.ToLower() ?? "";

        foreach (var node in AvailableNodes)
        {
            if (string.IsNullOrEmpty(search) || node.ToLower().Contains(search))
            {
                FilteredNodes.Add(node);
            }
        }
    }

    public void AddNodeAtPosition(string nodeType, double x, double y)
    {
        var nodeName = nodeType.Split('/').Last();
        var node = _pluginService.CreateNode(nodeName);
        if (node == null) return;

        if (node is NodeBase nb)
        {
            nb.X = x;
            nb.Y = y;
        }

        Graph.AddNode(node);
        Nodes.Add(new NodeViewModel(node));
        StatusMessage = $"Added node: {node.Name}";
        UpdateInspectionCards();
    }

    public void CreateConnection(NodeViewModel sourceNodeVm, NodeViewModel targetNodeVm)
    {
        var sourceNode = sourceNodeVm.Node;
        var targetNode = targetNodeVm.Node;

        if (sourceNode == null || targetNode == null) return;

        // Check if connection already exists
        var existingConnection = Graph.Connections.FirstOrDefault(c =>
            c.SourceNodeId == sourceNode.Id && c.TargetNodeId == targetNode.Id);

        if (existingConnection != null) return;

        // Create simple execution order connection
        if (!Graph.Connect(sourceNode, targetNode)) return;

        // Create visual connection
        var connection = Graph.Connections.Last();
        var connVm = new ConnectionViewModel(connection, sourceNodeVm, targetNodeVm);
        Connections.Add(connVm);

        StatusMessage = $"Connected {sourceNode.Name} -> {targetNode.Name}";
    }

    public void DeleteConnection(ConnectionViewModel connectionVm)
    {
        if (connectionVm == null) return;

        // Remove from graph
        Graph.Disconnect(connectionVm.Connection);

        // Remove from UI
        Connections.Remove(connectionVm);

        StatusMessage = "Connection deleted";
    }

    public void UpdateConnectionPositions()
    {
        foreach (var conn in Connections)
        {
            conn.UpdatePositions();
        }
    }

    public void DeleteNode(NodeViewModel nodeVm)
    {
        if (nodeVm == null) return;

        // If deleting the currently selected node, clear selection state first
        if (SelectedNode == nodeVm)
        {
            SelectNode(null);
        }

        // Remove connections referencing this node first
        var toRemove = Connections.Where(c =>
            c.Connection.SourceNodeId == nodeVm.Node.Id ||
            c.Connection.TargetNodeId == nodeVm.Node.Id).ToList();
        foreach (var connVm in toRemove)
        {
            Graph.Disconnect(connVm.Connection);
            Connections.Remove(connVm);
        }

        // Remove node from graph and UI
        Graph.RemoveNode(nodeVm.Node);
        Nodes.Remove(nodeVm);

        StatusMessage = $"Deleted node: {nodeVm.Name}";

        // Update visualizations to reflect deletion
        UpdateVisualization();
        UpdateAllRoiVisualizations();
        UpdateDetectedCircleVisualization();
        UpdateInspectionCards();
    }

    public void SelectNode(NodeViewModel? nodeVm)
    {
        // Deselect all nodes first
        foreach (var node in Nodes)
        {
            node.IsSelected = false;
        }

        // Default hide ROI filter toggle, circle detect button, and inspect button
        IsRoiFilterToggleVisible = false;
        _selectedRoiFilterNode = null;
        IsCircleDetectButtonVisible = false;
        _selectedCircleDetectionNode = null;
        IsInspectButtonVisible = false;
        _selectedInspectionNode = null;
        _forceFilterVisualization = false;

        // Select the new node
        if (nodeVm != null)
        {
            nodeVm.IsSelected = true;
            SelectedNode = nodeVm;

            // Enable ROI drawing mode if this is a ROI Draw node
            if (nodeVm.Name == "ROI Draw")
            {
                IsRoiDrawingMode = true;
                SelectedRoiNode = nodeVm;
                // Show only this specific ROI
                UpdateSingleRoiVisualization(nodeVm);
                StatusMessage = "ROI Drawing Mode: Click on 3D viewer to set ROI bounds";
            }
            else
            {
                IsRoiDrawingMode = false;
                SelectedRoiNode = null;

                if (nodeVm.Name == "Import Point Cloud")
                {
                    // Hide ROIs when viewing raw import data
                    Scene.UpdateRoiVisualizations(Enumerable.Empty<RoiVisualizationData>());
                }
                else
                {
                    // Show all ROIs when not in drawing mode
                    UpdateAllRoiVisualizations();
                }
            }

            // If ROI Filter node selected, show ON/OFF toggle
            if (nodeVm.Name == "ROI Filter")
            {
                IsRoiFilterToggleVisible = true;
                _selectedRoiFilterNode = nodeVm;
                _forceFilterVisualization = IsRoiFilterOn; // Apply current toggle state

                // Find connected ROI Draw node and show only its ROI
                var connectedDrawNode = FindConnectedRoiDrawNode(nodeVm);
                if (connectedDrawNode != null)
                {
                    UpdateSingleRoiVisualization(connectedDrawNode);
                }
                else
                {
                    // No specific ROI connected, show all
                    UpdateAllRoiVisualizations();
                }

                StatusMessage = IsRoiFilterOn ?
                    "ROI Filter ON: Showing filtered data only" :
                    "ROI Filter OFF: Showing all data";
            }

            // If Circle Detection node selected, auto-enable filter and show Detect button
            else if (nodeVm.Name == "Circle Detection")
            {
                IsCircleDetectButtonVisible = true;
                _selectedCircleDetectionNode = nodeVm;

                // Find the ROI Filter node connected to this Circle Detection node
                var roiFilterNode = Graph.Connections
                    .Where(c => c.TargetNodeId == nodeVm.Node.Id)
                    .Select(c => Graph.Nodes.FirstOrDefault(n => n.Id == c.SourceNodeId && n.Name == "ROI Filter"))
                    .FirstOrDefault();

                if (roiFilterNode != null)
                {
                    var roiFilterNodeVm = Nodes.FirstOrDefault(n => n.Node.Id == roiFilterNode.Id);
                    if (roiFilterNodeVm != null)
                    {
                        _selectedRoiFilterNode = roiFilterNodeVm;
                        IsRoiFilterOn = true; // Auto-enable filter
                        _forceFilterVisualization = true;
                        StatusMessage = "Circle Detection Mode: Auto-enabled ROI filter - showing filtered data only";

                        // Find connected ROI Draw node and show only its ROI
                        var connectedDrawNode = FindConnectedRoiDrawNode(roiFilterNodeVm);
                        if (connectedDrawNode != null)
                        {
                            UpdateSingleRoiVisualization(connectedDrawNode);
                        }
                        else
                        {
                            UpdateAllRoiVisualizations();
                        }
                    }
                }
                else
                {
                    StatusMessage = "Circle Detection selected: No ROI Filter connected";
                    UpdateAllRoiVisualizations();
                }
            }

            // If Inspection node selected, show Inspect button
            else if (nodeVm.Name == "Spec Inspection")
            {
                IsInspectButtonVisible = true;
                _selectedInspectionNode = nodeVm;
                StatusMessage = "Inspection Mode: Click 'Inspect' to validate detected circle against specifications";

                // Auto-enable filter visualization if connected to Circle Detection -> ROI Filter
                var circleNode = Graph.Connections
                    .Where(c => c.TargetNodeId == nodeVm.Node.Id)
                    .Select(c => Graph.Nodes.FirstOrDefault(n => n.Id == c.SourceNodeId && n.Name == "Circle Detection"))
                    .FirstOrDefault();

                if (circleNode != null)
                {
                    var roiFilterNode = Graph.Connections
                        .Where(c => c.TargetNodeId == circleNode.Id)
                        .Select(c => Graph.Nodes.FirstOrDefault(n => n.Id == c.SourceNodeId && n.Name == "ROI Filter"))
                        .FirstOrDefault();

                    if (roiFilterNode != null)
                    {
                        var roiFilterNodeVm = Nodes.FirstOrDefault(n => n.Node.Id == roiFilterNode.Id);
                        if (roiFilterNodeVm != null)
                        {
                            _selectedRoiFilterNode = roiFilterNodeVm;
                            IsRoiFilterOn = true;
                            _forceFilterVisualization = true;

                            var connectedDrawNode = FindConnectedRoiDrawNode(roiFilterNodeVm);
                            if (connectedDrawNode != null)
                            {
                                UpdateSingleRoiVisualization(connectedDrawNode);
                            }
                            else
                            {
                                UpdateAllRoiVisualizations();
                            }
                        }
                    }
                }
            }
        }
        else
        {
            // Deselecting all nodes - exit ROI drawing mode and show all ROIs
            SelectedNode = null;
            IsRoiDrawingMode = false;
            SelectedRoiNode = null;
            
            // IMPORTANT: Show all ROIs when nothing is selected
            UpdateAllRoiVisualizations();
            
            StatusMessage = "Ready - Showing all data";
        }

        // Update result panel visibility
        IsResultPanelVisible = IsCircleDetectButtonVisible || IsInspectButtonVisible;

        // Update visualization to reflect filter state
        UpdateVisualization();
        UpdateDetectedCircleVisualization();
    }

    private NodeViewModel? FindConnectedRoiDrawNode(NodeViewModel nodeVm)
    {
        if (nodeVm?.Node == null) return null;

        // Find an incoming connection from a ROI Draw node
        var sourceNodeModel = Graph.Connections
            .Where(c => c.TargetNodeId == nodeVm.Node.Id)
            .Select(c => Graph.Nodes.FirstOrDefault(n => n.Id == c.SourceNodeId && n.Name == "ROI Draw"))
            .FirstOrDefault();

        if (sourceNodeModel == null) return null;

        return Nodes.FirstOrDefault(n => n.Node.Id == sourceNodeModel.Id);
    }

    private void UpdateSingleRoiVisualization(NodeViewModel roiNodeVm)
    {
        if (roiNodeVm?.Node == null)
        {
            Scene.UpdateRoiVisualizations(Enumerable.Empty<RoiVisualizationData>());
            return;
        }

        var roiData = CreateRoiData(roiNodeVm);
        Scene.UpdateRoiVisualizations(new[] { roiData });
    }

    private void UpdateAllRoiVisualizations()
    {
        // Find all ROI Draw nodes
        var roiDrawNodes = Nodes.Where(n => n.Name == "ROI Draw").ToList();
        
        if (roiDrawNodes.Count == 0)
        {
            Scene.UpdateRoiVisualizations(Enumerable.Empty<RoiVisualizationData>());
            return;
        }

        var rois = roiDrawNodes.Select(CreateRoiData).ToList();
        Scene.UpdateRoiVisualizations(rois);
    }

    private RoiVisualizationData CreateRoiData(NodeViewModel roiNodeVm)
    {
        var centerX = GetParameterValue<float>(roiNodeVm.Node, "CenterX");
        var centerY = GetParameterValue<float>(roiNodeVm.Node, "CenterY");
        var centerZ = GetParameterValue<float>(roiNodeVm.Node, "CenterZ");
        var sizeX = GetParameterValue<float>(roiNodeVm.Node, "SizeX");
        var sizeY = GetParameterValue<float>(roiNodeVm.Node, "SizeY");
        var sizeZ = GetParameterValue<float>(roiNodeVm.Node, "SizeZ");
        var radius = GetParameterValue<float>(roiNodeVm.Node, "Radius");
        var shapeStr = GetParameterValue<string>(roiNodeVm.Node, "Shape") ?? "Box";

        var shape = shapeStr.ToLower() switch
        {
            "cylinder" => ROIShape.Cylinder,
            "sphere" => ROIShape.Sphere,
            _ => ROIShape.Box
        };

        return new RoiVisualizationData
        {
            NodeId = roiNodeVm.Node.Id,
            Center = new System.Numerics.Vector3(centerX, centerY, centerZ),
            Size = new System.Numerics.Vector3(sizeX, sizeY, sizeZ),
            Radius = radius,
            Shape = shape
        };
    }

    public void UpdateRoiFromParameters(NodeViewModel roiNodeVm)
    {
        if (roiNodeVm?.Node == null) return;

        // Update visualization based on whether this specific node is selected
        if (IsRoiDrawingMode && SelectedRoiNode == roiNodeVm)
        {
            UpdateSingleRoiVisualization(roiNodeVm);
        }
        else
        {
            UpdateAllRoiVisualizations();
        }
    }

    private void UpdateRoiVisualization(NodeViewModel roiNodeVm)
    {
        UpdateRoiFromParameters(roiNodeVm);
    }

    private T GetParameterValue<T>(INode node, string paramName)
    {
        var param = node.Parameters.FirstOrDefault(p => p.Name == paramName);
        if (param?.Value is T value)
            return value;
        return default!;
    }

    [RelayCommand]
    private void SaveWorkflow()
    {
        try
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Workflow (*.vpp.json)|*.vpp.json|JSON (*.json)|*.json",
                Title = "Save Workflow"
            };
            if (dlg.ShowDialog() != true) return;

            var dto = WorkflowDto.FromViewModels(Nodes, Connections);
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new Matrix4x4JsonConverter() }
            };
            var json = JsonSerializer.Serialize(dto, options);
            File.WriteAllText(dlg.FileName, json);
            StatusMessage = $"Workflow saved: {System.IO.Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void LoadWorkflow()
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Workflow (*.vpp.json)|*.vpp.json|JSON (*.json)|*.json",
                Title = "Load Workflow"
            };
            if (dlg.ShowDialog() != true) return;

            var json = File.ReadAllText(dlg.FileName);
            
            // Use custom JSON converter for Matrix4x4
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new Matrix4x4JsonConverter() }
            };
            
            var dto = JsonSerializer.Deserialize<WorkflowDto>(json, options);
            if (dto == null)
            {
                StatusMessage = "Invalid workflow file";
                return;
            }

            Graph = new NodeGraph();
            Nodes.Clear();
            Connections.Clear();

            var idToVm = new Dictionary<string, NodeViewModel>();
            var oldIdToNewId = new Dictionary<string, string>(); // Track ID mapping
            
            foreach (var n in dto.Nodes)
            {
                var node = _pluginService.CreateNode(n.Name);
                if (node == null)
                {
                    StatusMessage = $"Warning: Could not create node type '{n.Name}', skipping...";
                    continue;
                }
                
                if (node is NodeBase nb)
                {
                    nb.X = n.X;
                    nb.Y = n.Y;
                }
                
                // Store old ID to new ID mapping
                oldIdToNewId[n.Id] = node.Id;
                
                foreach (var p in n.Parameters)
                {
                    var param = node.Parameters.FirstOrDefault(x => x.Name == p.Name);
                    if (param != null && p.Value != null)
                    {
                        try
                        {
                            // Special handling for Matrix4x4
                            if (param.Type == typeof(System.Numerics.Matrix4x4))
                            {
                                if (p.Value is JsonElement jsonElement)
                                {
                                    param.Value = DeserializeMatrix4x4(jsonElement);
                                }
                                else if (p.Value is System.Numerics.Matrix4x4 matrix)
                                {
                                    param.Value = matrix;
                                }
                            }
                            else
                            {
                                // Handle JsonElement values
                                if (p.Value is JsonElement je)
                                {
                                    param.Value = ConvertJsonElement(je, param.Type);
                                }
                                else
                                {
                                    param.Value = Convert.ChangeType(p.Value, param.Type);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            StatusMessage = $"Warning: Could not set parameter {p.Name} on {n.Name}: {ex.Message}";
                        }
                    }
                }
                
                Graph.AddNode(node);
                var vm = new NodeViewModel(node);
                Nodes.Add(vm);
                idToVm[n.Id] = vm; // Map using old ID for connections
            }

            // Create connections using old IDs
            foreach (var c in dto.Connections)
            {
                var src = idToVm.GetValueOrDefault(c.SourceNodeId);
                var dst = idToVm.GetValueOrDefault(c.TargetNodeId);
                if (src != null && dst != null)
                {
                    if (Graph.Connect(src.Node, dst.Node))
                    {
                        var conn = Graph.Connections.Last();
                        var cvm = new ConnectionViewModel(conn, src, dst);
                        Connections.Add(cvm);
                    }
                }
            }

            StatusMessage = $"Workflow loaded: {System.IO.Path.GetFileName(dlg.FileName)} ({Nodes.Count} nodes, {Connections.Count} connections)";
            
            // Auto-execute to display all point clouds
            _ = ExecuteGraph();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load failed: {ex.Message}";
        }
    }

    private System.Numerics.Matrix4x4 DeserializeMatrix4x4(JsonElement element)
    {
        return new System.Numerics.Matrix4x4(
            element.GetProperty("M11").GetSingle(),
            element.GetProperty("M12").GetSingle(),
            element.GetProperty("M13").GetSingle(),
            element.GetProperty("M14").GetSingle(),
            element.GetProperty("M21").GetSingle(),
            element.GetProperty("M22").GetSingle(),
            element.GetProperty("M23").GetSingle(),
            element.GetProperty("M24").GetSingle(),
            element.GetProperty("M31").GetSingle(),
            element.GetProperty("M32").GetSingle(),
            element.GetProperty("M33").GetSingle(),
            element.GetProperty("M34").GetSingle(),
            element.GetProperty("M41").GetSingle(),
            element.GetProperty("M42").GetSingle(),
            element.GetProperty("M43").GetSingle(),
            element.GetProperty("M44").GetSingle()
        );
    }

    private object? ConvertJsonElement(JsonElement element, Type targetType)
    {
        if (targetType == typeof(string))
            return element.GetString();
        if (targetType == typeof(int))
            return element.GetInt32();
        if (targetType == typeof(float))
            return element.GetSingle();
        if (targetType == typeof(double))
            return element.GetDouble();
        if (targetType == typeof(bool))
            return element.GetBoolean();
        
        return element.ToString();
    }

    private class WorkflowDto
    {
        public List<NodeDto> Nodes { get; set; } = new();
        public List<ConnectionDto> Connections { get; set; } = new();

        public static WorkflowDto FromGraph(NodeGraph graph)
        {
            var dto = new WorkflowDto();
            foreach (var node in graph.Nodes)
            {
                var nd = new NodeDto
                {
                    Id = node.Id,
                    Name = node.Name,
                    Category = node.Category,
                };
                if (node is NodeBase nb)
                {
                    nd.X = nb.X;
                    nd.Y = nb.Y;
                }
                foreach (var p in node.Parameters)
                {
                    nd.Parameters.Add(new ParameterDto
                    {
                        Name = p.Name,
                        Value = p.Value
                    });
                }
                dto.Nodes.Add(nd);
            }
            foreach (var c in graph.Connections)
            {
                dto.Connections.Add(new ConnectionDto
                {
                    SourceNodeId = c.SourceNodeId,
                    TargetNodeId = c.TargetNodeId
                });
            }
            return dto;
        }

        public static WorkflowDto FromViewModels(IEnumerable<NodeViewModel> nodes, IEnumerable<ConnectionViewModel> connections)
        {
            var dto = new WorkflowDto();
            foreach (var nodeVm in nodes)
            {
                var nd = new NodeDto
                {
                    Id = nodeVm.Node.Id,
                    Name = nodeVm.Name,
                    Category = nodeVm.Category,
                    X = nodeVm.X,
                    Y = nodeVm.Y
                };

                foreach (var p in nodeVm.Parameters)
                {
                    nd.Parameters.Add(new ParameterDto
                    {
                        Name = p.Name,
                        Value = p.Value
                    });
                }
                dto.Nodes.Add(nd);
            }

            foreach (var connVm in connections)
            {
                dto.Connections.Add(new ConnectionDto
                {
                    SourceNodeId = connVm.Connection.SourceNodeId,
                    TargetNodeId = connVm.Connection.TargetNodeId
                });
            }
            return dto;
        }
    }

    private class NodeDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public List<ParameterDto> Parameters { get; set; } = new();
    }

    private class ParameterDto
    {
        public string Name { get; set; } = string.Empty;
        public object? Value { get; set; }
    }

    private class ConnectionDto
    {
        public string SourceNodeId { get; set; } = string.Empty;
        public string TargetNodeId { get; set; } = string.Empty;
        public string SourcePortName { get; set; } = string.Empty;
        public string TargetPortName { get; set; } = string.Empty;
    }
}

// Custom JSON converter for Matrix4x4
public class Matrix4x4JsonConverter : JsonConverter<System.Numerics.Matrix4x4>
{
    public override System.Numerics.Matrix4x4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
        {
            var root = doc.RootElement;
            return new System.Numerics.Matrix4x4(
                root.GetProperty("M11").GetSingle(),
                root.GetProperty("M12").GetSingle(),
                root.GetProperty("M13").GetSingle(),
                root.GetProperty("M14").GetSingle(),
                root.GetProperty("M21").GetSingle(),
                root.GetProperty("M22").GetSingle(),
                root.GetProperty("M23").GetSingle(),
                root.GetProperty("M24").GetSingle(),
                root.GetProperty("M31").GetSingle(),
                root.GetProperty("M32").GetSingle(),
                root.GetProperty("M33").GetSingle(),
                root.GetProperty("M34").GetSingle(),
                root.GetProperty("M41").GetSingle(),
                root.GetProperty("M42").GetSingle(),
                root.GetProperty("M43").GetSingle(),
                root.GetProperty("M44").GetSingle()
            );
        }
    }

    public override void Write(Utf8JsonWriter writer, System.Numerics.Matrix4x4 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("M11", value.M11);
        writer.WriteNumber("M12", value.M12);
        writer.WriteNumber("M13", value.M13);
        writer.WriteNumber("M14", value.M14);
        writer.WriteNumber("M21", value.M21);
        writer.WriteNumber("M22", value.M22);
        writer.WriteNumber("M23", value.M23);
        writer.WriteNumber("M24", value.M24);
        writer.WriteNumber("M31", value.M31);
        writer.WriteNumber("M32", value.M32);
        writer.WriteNumber("M33", value.M33);
        writer.WriteNumber("M34", value.M34);
        writer.WriteNumber("M41", value.M41);
        writer.WriteNumber("M42", value.M42);
        writer.WriteNumber("M43", value.M43);
        writer.WriteNumber("M44", value.M44);
        writer.WriteEndObject();
    }
}

public partial class NodeViewModel : ObservableObject
{
    public INode Node { get; }

    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isExecuting;

    public string Name => Node.Name;
    public string Category => Node.Category;
    public IReadOnlyList<IParameter> Parameters => Node.Parameters;

    public NodeViewModel(INode node)
    {
        Node = node;
        if (node is NodeBase nb)
        {
            X = nb.X;
            Y = nb.Y;
        }
    }
}

public partial class ConnectionViewModel : ObservableObject
{
    public Connection Connection { get; }
    private readonly NodeViewModel _sourceNodeVm;
    private readonly NodeViewModel _targetNodeVm;

    [ObservableProperty] private System.Windows.Point _startPoint;
    [ObservableProperty] private System.Windows.Point _endPoint;
    [ObservableProperty] private System.Windows.Point _controlPoint1;
    [ObservableProperty] private System.Windows.Point _controlPoint2;

    public System.Windows.Media.PathGeometry PathGeometry
    {
        get
        {
            var figure = new System.Windows.Media.PathFigure { StartPoint = StartPoint };
            figure.Segments.Add(new System.Windows.Media.BezierSegment(ControlPoint1, ControlPoint2, EndPoint, true));
            return new System.Windows.Media.PathGeometry(new[] { figure });
        }
    }

    partial void OnStartPointChanged(System.Windows.Point value) => OnPropertyChanged(nameof(PathGeometry));
    partial void OnEndPointChanged(System.Windows.Point value) => OnPropertyChanged(nameof(PathGeometry));
    partial void OnControlPoint1Changed(System.Windows.Point value) => OnPropertyChanged(nameof(PathGeometry));
    partial void OnControlPoint2Changed(System.Windows.Point value) => OnPropertyChanged(nameof(PathGeometry));

    public ConnectionViewModel(Connection connection, NodeViewModel sourceNodeVm, NodeViewModel targetNodeVm)
    {
        Connection = connection;
        _sourceNodeVm = sourceNodeVm;
        _targetNodeVm = targetNodeVm;
        UpdatePositions();
    }

    public void UpdatePositions()
    {
        if (_sourceNodeVm == null || _targetNodeVm == null) return;

        const double nodeWidth = 150;
        const double titleBarHeight = 30;
        const double portRowHeight = 28;
        const double portVerticalOffset = titleBarHeight + (portRowHeight / 2);

        StartPoint = new System.Windows.Point(
            _sourceNodeVm.X + nodeWidth,
            _sourceNodeVm.Y + portVerticalOffset);

        EndPoint = new System.Windows.Point(
            _targetNodeVm.X,
            _targetNodeVm.Y + portVerticalOffset);

        var controlOffset = Math.Abs(EndPoint.X - StartPoint.X) / 2;
        ControlPoint1 = new System.Windows.Point(StartPoint.X + controlOffset, StartPoint.Y);
        ControlPoint2 = new System.Windows.Point(EndPoint.X - controlOffset, EndPoint.Y);
    }
}
