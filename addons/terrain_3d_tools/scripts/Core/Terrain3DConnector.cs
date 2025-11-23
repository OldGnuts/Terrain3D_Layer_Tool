// /Core/Terrain3DConnector.cs
using Godot;
using Terrain3DWrapper;

namespace Terrain3DTools.Core
{
    /// <summary>
    /// Manages the connection to the Terrain3D node, including auto-discovery,
    /// validation, and providing access to Terrain3D properties.
    /// </summary>
    public class Terrain3DConnector
    {
        private Node3D _terrain3DNode;
        private Terrain3D _terrain3D;
        private readonly Node _ownerNode;

        public Terrain3DConnector(Node ownerNode)
        {
            _ownerNode = ownerNode;
        }

        #region Properties
        /// <summary>
        /// The Terrain3D node being managed. Setting this validates and wraps the node.
        /// </summary>
        public Node3D Terrain3DNode
        {
            get => _terrain3DNode;
            set => SetTerrain3DNode(value);
        }

        /// <summary>
        /// The wrapped Terrain3D instance. Null if not connected.
        /// </summary>
        public Terrain3D Terrain3D => _terrain3D;

        /// <summary>
        /// Whether a valid Terrain3D connection is established.
        /// </summary>
        public bool IsConnected => _terrain3D != null && _terrain3DNode != null;

        /// <summary>
        /// The region size from Terrain3D. Returns 0 if not connected.
        /// </summary>
        public int RegionSize => _terrain3D?.RegionSize ?? 0;

        /// <summary>
        /// The mesh vertex spacing from Terrain3D. Returns 0 if not connected.
        /// </summary>
        public float MeshVertexSpacing => _terrain3D?.MeshVertexSpacing ?? 0f;
        #endregion

        #region Connection Management
        /// <summary>
        /// Attempts to automatically find and connect to a Terrain3D node in the scene.
        /// Returns true if a Terrain3D node was found and connected.
        /// </summary>
        public bool AutoConnect()
        {
            if (_ownerNode == null)
            {
                GD.PrintErr("[Terrain3DConnector] Cannot auto-connect: owner node is null");
                return false;
            }

            var nodes = _ownerNode.FindChildren("*", "Terrain3D");
            if (nodes.Count > 0)
            {
                var foundNode = nodes[0] as Node3D;
                if (foundNode != null)
                {
                    Terrain3DNode = foundNode;
                    
                    if (IsConnected)
                    {
                        GD.Print($"[Terrain3DConnector] Auto-connected to Terrain3D node: {foundNode.Name}");
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Validates that the Terrain3D connection is still valid.
        /// Returns false if the node was freed or is no longer valid.
        /// </summary>
        public bool ValidateConnection()
        {
            if (!IsConnected)
                return false;

            if (!GodotObject.IsInstanceValid(_terrain3DNode))
            {
                GD.PrintErr("[Terrain3DConnector] Terrain3D node is no longer valid");
                Disconnect();
                return false;
            }

            return true;
        }

        /// <summary>
        /// Disconnects from the current Terrain3D node.
        /// </summary>
        public void Disconnect()
        {
            _terrain3DNode = null;
            _terrain3D = null;
        }
        #endregion

        #region Private Methods
        private void SetTerrain3DNode(Node3D value)
        {
            // No change if setting to the same node
            if (_terrain3DNode == value) return;

            // Disconnect if setting to null
            if (value == null)
            {
                Disconnect();
                return;
            }

            // Validate the node is actually a Terrain3D
            if (!Terrain3DNodeVerifier.CheckTerrain3D(value))
            {
                GD.PrintErr($"[Terrain3DConnector] Node '{value.Name}' is not a valid Terrain3D node");
                Disconnect();
                return;
            }

            // Connect to the new node
            _terrain3DNode = value;
            _terrain3D = new Terrain3D(_terrain3DNode as GodotObject);

            if (IsConnected)
            {
                GD.Print($"[Terrain3DConnector] Connected to Terrain3D node: {value.Name}");
                GD.Print($"  Region Size: {RegionSize}");
                GD.Print($"  Vertex Spacing: {MeshVertexSpacing}");
            }
            else
            {
                GD.PrintErr("[Terrain3DConnector] Failed to create Terrain3D wrapper");
                Disconnect();
            }
        }
        #endregion

        #region Query Methods
        /// <summary>
        /// Returns a string describing the current connection status.
        /// Useful for debugging and UI display.
        /// </summary>
        public string GetConnectionStatus()
        {
            if (!IsConnected)
                return "Not connected";

            if (!ValidateConnection())
                return "Connection invalid";

            return $"Connected to '{_terrain3DNode.Name}' (Region: {RegionSize}, Spacing: {MeshVertexSpacing})";
        }
        #endregion
    }
}