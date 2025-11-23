using Godot;
using System;
using System.IO;

namespace Terrain3DWrapper
{
    [GlobalClass, Tool]
    public partial class Terrain3DWrapperTest : Node3D
    {
        [Export]
        public Node3D terrain3DNode
        {
            get
            {
                return _terrain3DNode;
            }
            set
            {
                _terrain3DNode = value;
                _terrain3DInitialized = Init();
                if (_terrain3DInitialized)
                    RunTerrain3DTest();
            }
        }

        private Node3D _terrain3DNode;
        bool _terrain3DInitialized = false;
        public Terrain3D terrain3D;

        public bool Init()
        {
            if (terrain3DNode != null)
            {
                if (Terrain3DNodeVerifier.CheckTerrain3D(terrain3DNode))
                {
                    terrain3D = new Terrain3D(terrain3DNode as GodotObject);
                    return true;
                }
                return false;
            }
            return false;
        }

        public void RunTerrain3DTest()
        {
            GD.Print("Region size :" + terrain3D.RegionSize);
            GD.Print("Region count :" + terrain3D.Data.GetRegionCount());
            int index = 0;
            if (terrain3D.Data.GetRegionCount() > 0)
            {
                foreach (Image image in terrain3D.Data.HeightMaps)
                {
                    string testPath = "res://TestData";
                    string absolutePath = ProjectSettings.GlobalizePath(testPath);
                    CreateFolderIfNotExist(absolutePath);

                    if (image != null)
                    {
                        string filePath = testPath + "/" + index + ".png";
                        Error error_code = image.SavePng(filePath);

                        if (error_code == Error.Ok)
                            GD.Print("Image saved successfully to: " + filePath);
                        else
                            GD.Print("Error saving image: " + error_code);
                        index++;
                    }
                }
            }
        }
        private void CreateFolderIfNotExist(string path)
        {
            if (!Directory.Exists(path))
            {
                try
                {
                    Directory.CreateDirectory(path);
                    GD.Print($"Folder created: {path}");
                }
                catch (Exception e)
                {
                    GD.PrintErr($"Error creating folder {path}: {e.Message}");
                }
            }
            else
            {
                GD.Print($"Folder already exists: {path}");
            }
        }

    }
}