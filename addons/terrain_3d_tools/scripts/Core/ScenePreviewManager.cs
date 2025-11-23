// /Core/ScenePreviewManager.cs

using Godot;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Core
{
    public class ScenePreviewManager
    {
        private const string DEBUG_CLASS_NAME = "ScenePreviewManager";
        private const string PREVIEW_PARENT_NAME = "ScenePreviews";
        private Node3D _previewParent;
        private readonly Node3D _owner;

        public ScenePreviewManager(Node3D owner)
        {
            _owner = owner;
            DebugManager.Instance?.RegisterClass(DEBUG_CLASS_NAME);
        }

        public Node3D PreviewParent => _previewParent;

        public void Initialize()
        {
            DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.Initialization, "Initialize");
            
            // Always look for and clean up any existing preview parent first
            CleanupExistingPreviewParent();
            
            // Create fresh preview parent
            CreatePreviewParent();
            
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Initialization, 
                "Initialized with fresh preview parent");
            
            DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.Initialization, "Initialize");
        }

        private void CleanupExistingPreviewParent()
        {
            // Try to find any existing ScenePreviews node
            Node3D existing = null;
            
            // Method 1: Direct child
            existing = _owner.GetNodeOrNull<Node3D>(PREVIEW_PARENT_NAME);
            
            // Method 2: FindChild
            if (existing == null)
            {
                existing = _owner.FindChild(PREVIEW_PARENT_NAME, recursive: false, owned: false) as Node3D;
            }
            
            // Method 3: Manual search
            if (existing == null)
            {
                foreach (var child in _owner.GetChildren())
                {
                    if (child.Name == PREVIEW_PARENT_NAME)
                    {
                        existing = child as Node3D;
                        break;
                    }
                }
            }

            // Free it if found
            if (existing != null && GodotObject.IsInstanceValid(existing))
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Cleanup, 
                    $"Cleaning up existing preview parent with {existing.GetChildCount()} children");
                existing.QueueFree();
            }
        }

        private void CreatePreviewParent()
        {
            _previewParent = new Node3D { Name = PREVIEW_PARENT_NAME };
            _owner.AddChild(_previewParent);
            
            // Don't set Owner - keep it transient
            // This ensures it's not saved with the scene
            
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Initialization, 
                "Created new transient preview parent");
        }

        public void Cleanup()
        {
            if (_previewParent != null && GodotObject.IsInstanceValid(_previewParent))
            {
                DebugManager.Instance?.StartTimer(DEBUG_CLASS_NAME, DebugCategory.Cleanup, "Cleanup");
                
                int childCount = _previewParent.GetChildCount();
                
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.Cleanup, 
                    $"Freeing preview parent with {childCount} children");
                
                // Free all children first
                foreach (Node child in _previewParent.GetChildren())
                {
                    if (GodotObject.IsInstanceValid(child))
                    {
                        child.QueueFree();
                    }
                }
                
                // Free the parent itself
                _previewParent.QueueFree();
                _previewParent = null;
                
                DebugManager.Instance?.EndTimer(DEBUG_CLASS_NAME, DebugCategory.Cleanup, "Cleanup");
            }
        }
    }
}