﻿// Project:         Daggerfall Tools For Unity
// Copyright:       Copyright (C) 2009-2016 Daggerfall Workshop
// Web Site:        http://www.dfworkshop.net
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Source Code:     https://github.com/Interkarma/daggerfall-unity
// Original Author: Michael Rauter (a.k.a. Nystul)
// Contributors:    Interkarma
// 
// Notes:
//

//#define DEBUG_Nameplates

using UnityEngine;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.Player;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility;
using Wenzil.Console;

namespace DaggerfallWorkshop.Game
{
    /// <summary>
    /// this class provides the automap core functionality like geometry creation and discovery mechanism 
    /// </summary>
    public class DaggerfallExteriorAutomap : MonoBehaviour
    {
        #region Singleton
        private static DaggerfallExteriorAutomap _instance;

        public static DaggerfallExteriorAutomap instance
        {
            get
            {
                if (_instance == null)
                    _instance = GameObject.FindObjectOfType<DaggerfallExteriorAutomap>();
                return _instance;
            }
            private set { _instance = value; }
        }
        #endregion

        #region Fields

        GameObject gameobjectExteriorAutomap = null; // used to hold reference to instance of GameObject "ExteriorAutomap" (which has script Game/DaggerfallExteriorAutomap.cs attached)

        GameObject gameobjectCustomCanvas = null; // used to hold reference to instance of GameObject with the custom canvas used for drawing the exterior automap        

        int layerAutomap; // layer used for level geometry of automap

        GameObject gameObjectCameraExteriorAutomap = null; // used to hold reference to GameObject to which camera class for automap camera is attached to
        Camera cameraExteriorAutomap = null; // camera for exterior automap camera        
        Quaternion cameraTransformRotationSaved; // camera rotation is saved so that after closing and reopening exterior automap the camera transform settings can be restored
        float cameraOrthographicSizeSaved; // camera's orthographic size is saved so that after closing and reopening exterior automap the camera settings can be restored

        GameObject gameObjectPlayerAdvanced = null; // used to hold reference to instance of GameObject "PlayerAdvanced"

        // exterior automap view mode (controls settings for extra buildings and ground flats)
        public enum ExteriorAutomapViewMode
        {
            Original = 0,
            Extra = 1,
            All = 2
        };

        ExteriorAutomapViewMode currentExteriorAutomapViewMode = ExteriorAutomapViewMode.Original; // currently selected exterior automap view mode     

        // flag that indicates if external script should reset automap settings (set via Property ResetAutomapSettingsSignalForExternalScript checked and erased by DaggerfallExteriorAutomapWindow script)
        // this might look weirds - why not just notify the DaggerfallExteriorAutomapWindow class you may ask... - I wanted to make DaggerfallExteriorAutomap inaware and independent of the actual GUI implementation
        // so communication will always be only from DaggerfallExteriorAutomapWindow to DaggerfallExteriorAutomap class - so into other direction it works in that way that DaggerfallExteriorAutomap will pull
        // from DaggerfallExteriorAutomapWindow via flags - this is why this flag and its Property ResetAutomapSettingsSignalForExternalScript exist
        bool resetAutomapSettingsFromExternalScript = false;

        GameObject gameobjectPlayerMarkerArrow = null; // GameObject which will hold player marker arrow
        GameObject gameobjectPlayerMarkerArrowStamp = null; // GameObject which will hold player marker arrow stamp
        GameObject gameobjectPlayerMarkerCircle = null; // GameObject which will hold player marker circle (actually a cylinder)

        // IK: Change to non-const to suppress compile-time warning
        bool allowRightAlignedNameplates = false;

        const float nameplatesPlacementDepth = 4.0f; // the height at which the nameplates are positioned - set to be higher than player marker and layout texture

        DaggerfallFont customFont = null; // custom font used so that the Texture2D texture of the textlabel which is used for nameplates will have yellow colored text in it

        // this structure defines all the infos used for building nameplates (appearance and placement)
        private struct BuildingNameplate
        {
            public Vector2 anchorPoint; // the anchor point of the nameplate (the position in world coordinates of the nameplate - target position for the gameobject's transform position)
            public string name; // the name of the nameplate (this is never changed - even if nameplate is later presented as "*" in case of an occlusion - this remains the (unchanged) full name)
            public int uniqueIndex; // the unique index of the nameplate in the array of nameplates
            public TextLabel textLabel; // the TextLabel used to create and reuse its Texture2D element as texture for the nameplate 
            public GameObject gameObject; // the GameObject used to hold the nameplate     
            public float scale; // the scale of the nameplates (target scale for the gamobject's scale)
            public Vector2 offset; // offset in world coordinates of nameplate (this is computed to prevent collision of nameplates where possible)
            public float width; // width of the nameplate after applying scale
            public float height; // height of the nameplate after applying scale
            public Vector2 upperLeftCorner; // (rotated) upper left corner of the nameplate (as if anchor point would lie in the world origin - world position of the corner can be computed by adding anchorpoint/nameplate position)
            public Vector2 upperRightCorner; // (rotated) upper right corner of the nameplate (as if anchor point would lie in the world origin - world position of the corner can be computed by adding anchorpoint/nameplate position)
            public Vector2 lowerLeftCorner; // (rotated) lower left corner of the nameplate (as if anchor point would lie in the world origin - world position of the corner can be computed by adding anchorpoint/nameplate position)
            public Vector2 lowerRightCorner; // (rotated) lower right corner of the nameplate (as if anchor point would lie in the world origin - world position of the corner can be computed by adding anchorpoint/nameplate position)
            public int numCollisionsDetected; // this field is used to store the intermediate results for the number of collision detected for this nameplate (while performing the nameplate collision checks), this value may change while the collision detection is performed and should not be trusted after it has finished
            public bool placed; // used in the collision detection step to mark nameplates that were already fixed/placed
            public bool nameplateReplaced; // used to mark nameplates that have been replaced by a placeholder "*" nameplate
#if DEBUG_Nameplates
            public GameObject anchorLine; // offset line to anchorpoint
            public GameObject debugLine1; // debug line for nameplate going from top left to bottom right corner
            public GameObject debugLine2; // debug line for nameplate going from bottom left to top right corner
#endif
        }

        BuildingNameplate[] buildingNameplates = null; // the array of nameplates for the current loaded location

        GameObject gameObjectBuildingNameplates = null; // parent gameobject for all building name plates

        GameObject popUpNameplate = null; // the pop-up nameplate used to expand "*" nameplates

        public struct Rectangle
        {
            public int xpos;
            public int ypos;
            public int width;
            public int height;
        }

        /// <summary>
        /// Block layout of location.
        /// </summary>
        public struct BlockLayout
        {
            public int x;
            public int y;
            public Rectangle rect;
            public string name;
            public DFBlock.BlockTypes blocktype;
            public DFBlock.RdbTypes rdbType;
        }

        DFLocation location;

        // location dimensions
        int locationWidth;
        int locationHeight;

        public const int blockSizeWidth = 64;
        public const int blockSizeHeight = 64;

        public const int numMaxBlocksX = 8;
        public const int numMaxBlocksY = 8;

        // layout image dimensions
        int layoutWidth;
        int layoutHeight;

        private const float layoutMultiplier = 1.0f;

        private BlockLayout[] exteriorLayout = null;

        Texture2D exteriorLayoutTexture = null;

        bool revealUndiscoveredBuildings = false;

        #endregion

        #region Properties

        /// <summary>
        /// DaggerfallExteriorAutomapWindow script will use this to get automap layer
        /// </summary>
        public int LayerAutomap
        {
            get { return (layerAutomap); }
        }

        /// <summary>
        /// DaggerfallExteriorAutomapWindow script will use this to get automap camera
        /// </summary>
        public Camera CameraExteriorAutomap
        {
            get { return (cameraExteriorAutomap); }
        }

        public float LayoutMultiplier
        {
            get { return (layoutMultiplier); }
        }

        public BlockLayout[] ExteriorLayout
        {
            get { if (exteriorLayout == null) { loadAndCreateLocationExteriorAutomap(); }; return (exteriorLayout); }
        }

        public int LocationWidth
        {
            get { return (locationWidth); }
        }

        public int LocationHeight
        {
            get { return (locationHeight); }
        }

        public GameObject GameobjectPlayerMarkerArrow
        {
            get { return (gameobjectPlayerMarkerArrow); }
        }

        /// <summary>
        /// DaggerfallExteriorAutomapWindow script will use this to check if it should reset automap settings (and if it does it will erase flag)
        /// </summary>
        public bool ResetAutomapSettingsSignalForExternalScript
        {
            get { return (resetAutomapSettingsFromExternalScript); }
            set { resetAutomapSettingsFromExternalScript = value; }
        }

        /// <summary>
        /// property for flag for console debug mode for revealing undiscovered buildings
        /// </summary>
        public bool RevealUndiscoveredBuildings
        {
            get { return (revealUndiscoveredBuildings); }
            set { revealUndiscoveredBuildings = value; }
        }
        

        #endregion

        #region Public Methods

        /// <summary>
        /// DaggerfallExteriorAutomapWindow script will use this to signal this script to update when automap window was pushed - TODO: check if this can done with an event (if events work with gui windows)
        /// </summary>
        public void updateAutomapStateOnWindowPush()
        {
            // create camera (if not present) that will render automap level geometry
            createExteriorAutomapCamera();

            // restore camera rotation and zoom
            gameObjectCameraExteriorAutomap.transform.rotation = cameraTransformRotationSaved;
            cameraExteriorAutomap.orthographicSize = cameraOrthographicSizeSaved;

            // recreate building nameplates (since a discovery could have happened since exterior automap has been opened last time)
            createBuildingNameplates(location);

            // focus player position
            cameraExteriorAutomap.transform.position = GameobjectPlayerMarkerArrow.transform.position + new Vector3(0.0f, 10.0f, 0.0f);

            // update player marker position and rotation
            updatePlayerMarker();
        }

        /// <summary>
        /// DaggerfallExteriorAutomapWindow script will use this to signal this script to update when automap window was popped - TODO: check if this can done with an event (if events work with gui windows)
        /// </summary>
        public void updateAutomapStateOnWindowPop()
        {
            cameraTransformRotationSaved = gameObjectCameraExteriorAutomap.transform.rotation;
            cameraOrthographicSizeSaved = cameraExteriorAutomap.orthographicSize;

            // destroy the camera so it does not use system resources
            if (gameObjectCameraExteriorAutomap != null)
            {
                UnityEngine.Object.Destroy(gameObjectCameraExteriorAutomap);
            }
        }

        public void switchToNextExteriorAutomapViewMode()
        {
            int numberOfExteriorAutomapViewModes = Enum.GetNames(typeof(ExteriorAutomapViewMode)).Length;
            currentExteriorAutomapViewMode++;
            if ((int)currentExteriorAutomapViewMode > numberOfExteriorAutomapViewModes - 1) // first mode is mode 0 -> so use numberOfExteriorAutomapViewModes-1 for comparison
                currentExteriorAutomapViewMode = 0;
            switch (currentExteriorAutomapViewMode)
            {
                default:
                case ExteriorAutomapViewMode.Original:
                    switchToExteriorAutomapViewModeOriginal();
                    break;
                case ExteriorAutomapViewMode.Extra:
                    switchToExteriorAutomapViewModeExtra();
                    break;
                case ExteriorAutomapViewMode.All:
                    switchToExteriorAutomapViewModeAll();
                    break;
            }
        }

        public void switchToExteriorAutomapViewModeOriginal()
        {
            currentExteriorAutomapViewMode = ExteriorAutomapViewMode.Original;
            createExteriorLayoutTexture(location, false, true, false);
            assignExteriorLayoutTextureToCustomCanvas();
        }

        public void switchToExteriorAutomapViewModeExtra()
        {
            currentExteriorAutomapViewMode = ExteriorAutomapViewMode.Extra;
            createExteriorLayoutTexture(location, true, true, false);
            assignExteriorLayoutTextureToCustomCanvas();
        }

        public void switchToExteriorAutomapViewModeAll()
        {
            currentExteriorAutomapViewMode = ExteriorAutomapViewMode.All;
            createExteriorLayoutTexture(location, true, false, false);
            assignExteriorLayoutTextureToCustomCanvas();
        }

        public enum LocationBorder
        {
            Top = 0,
            Bottom = 1,
            Left = 2,
            Right = 3
        };

        public Vector3 getLocationBorderPos(LocationBorder locationBorder)
        {
            int locationWidth = (int)(layoutWidth * layoutMultiplier);
            int locationHeight = (int)(layoutHeight * layoutMultiplier);
            Vector3 pos = Vector3.zero;
            switch (locationBorder)
            {
                case LocationBorder.Top:
                    pos.z = +locationHeight * 0.5f;
                    break;
                case LocationBorder.Bottom:
                    pos.z = -locationHeight * 0.5f;
                    break;
                case LocationBorder.Left:
                    pos.x = -locationWidth * 0.5f;
                    break;
                case LocationBorder.Right:
                    pos.x = +locationWidth * 0.5f;
                    break;
            }
            return (pos);
        }

        public void rotateBuildingNameplates(float angle)
        {
            undoNameplateOffsets();
            for (int i = 0; i < buildingNameplates.Length; i++)
            {
                BuildingNameplate buildingNameplate = buildingNameplates[i];

                buildingNameplate.gameObject.transform.Rotate(new Vector3(0.0f, angle, 0.0f));

                /*
                // rotate stored corners of nameplates (they are used for collision computations)
                buildingNameplate.upperLeftCorner = Quaternion.Euler(0, 0, -angle) * buildingNameplate.upperLeftCorner;
                buildingNameplate.upperRightCorner = Quaternion.Euler(0, 0, -angle) * buildingNameplate.upperRightCorner;
                buildingNameplate.lowerLeftCorner = Quaternion.Euler(0, 0, -angle) * buildingNameplate.lowerLeftCorner;
                buildingNameplate.lowerRightCorner = Quaternion.Euler(0, 0, -angle) * buildingNameplate.lowerRightCorner;
                */

                // rotate stored corners of nameplates (used for collision computations)
                buildingNameplate.upperLeftCorner = Quaternion.AngleAxis(-angle, Vector3.forward) * buildingNameplate.upperLeftCorner;
                buildingNameplate.upperRightCorner = Quaternion.AngleAxis(-angle, Vector3.forward) * buildingNameplate.upperRightCorner;
                buildingNameplate.lowerLeftCorner = Quaternion.AngleAxis(-angle, Vector3.forward) * buildingNameplate.lowerLeftCorner;
                buildingNameplate.lowerRightCorner = Quaternion.AngleAxis(-angle, Vector3.forward) * buildingNameplate.lowerRightCorner;

                buildingNameplates[i] = buildingNameplate;
            }
            computeNameplateOffsets();
            applyNameplateOffsets();
        }

        public void resetRotationBuildingNameplates()
        {
            undoNameplateOffsets();
            for (int i = 0; i < buildingNameplates.Length; i++)
            {
                BuildingNameplate buildingNameplate = buildingNameplates[i];
                buildingNameplate.gameObject.transform.rotation = Quaternion.Euler(Vector3.zero); // reset rotation
                // reset corners of nameplates (used for collision computations)
                buildingNameplate.upperLeftCorner = new Vector2(0.0f, +buildingNameplate.height * 0.5f);
                buildingNameplate.upperRightCorner = new Vector2(buildingNameplate.width, +buildingNameplate.height * 0.5f);
                buildingNameplate.lowerLeftCorner = new Vector2(0.0f, -buildingNameplate.height * 0.5f);
                buildingNameplate.lowerRightCorner = new Vector2(buildingNameplate.width, -buildingNameplate.height * 0.5f);
                buildingNameplates[i] = buildingNameplate;
            }
            computeNameplateOffsets();
            applyNameplateOffsets();
        }

        #endregion

        #region Unity

        void Start()
        {
            // Creates a new DaggerfallFont using default typeface
            customFont = new DaggerfallFont();
            // Reloads glyphs to Daggerfall's default yellowish text colour
            customFont.ReloadFont(DaggerfallUI.DaggerfallDefaultTextColor);

            // register console commands
            try
            {
                ExteriorAutoMapConsoleCommands.RegisterCommands();
            }
            catch (Exception ex)
            {
                Debug.LogError(string.Format("Error Registering Exterior Automap Console commands: {0}", ex.Message));

            }
        }

        void Awake()
        {
            gameObjectPlayerAdvanced = GameObject.Find("PlayerAdvanced");
            if (!gameObjectPlayerAdvanced)
            {
                DaggerfallUnity.LogMessage("GameObject \"PlayerAdvanced\" not found! in script DaggerfallExteriorAutomap (in function Awake())", true);
                if (Application.isEditor)
                    Debug.Break();
                else
                    Application.Quit();
            }

            gameobjectExteriorAutomap = GameObject.Find("Automap/ExteriorAutomap");
            if (gameobjectExteriorAutomap == null)
            {
                DaggerfallUnity.LogMessage("GameObject \"Automap/ExteriorAutomap\" missing! Create a GameObject called \"Automap\" in root of hierarchy and add a GameObject \"ExteriorAutomap\" to it, to this add script Game/DaggerfallExteriorAutomap!\"", true);
                if (Application.isEditor)
                    Debug.Break();
                else
                    Application.Quit();
            }

            layerAutomap = LayerMask.NameToLayer("ExteriorAutomap");
            if (layerAutomap == -1)
            {
                DaggerfallUnity.LogMessage("Did not find Layer with name \"Automap\"! Defaulting to Layer 10\nIt is prefered that Layer \"Automap\" is set in Unity Editor under \"Edit/Project Settings/Tags and Layers!\"", true);
                layerAutomap = 10;
            }

            Camera.main.cullingMask = Camera.main.cullingMask & ~((1 << layerAutomap)); // don't render automap layer with main camera

            cameraTransformRotationSaved = Quaternion.identity;
            cameraOrthographicSizeSaved = 10.0f; // dummy value > 0.0f -> will be overwritten once camera zoom is applied

            // important that transition events/delegates are created in Awake() instead of OnEnable (since exteriorAutomap gameobject is disabled when going indoors and enabled when going outdoors)
            PlayerGPS.OnMapPixelChanged += OnMapPixelChanged;
            PlayerEnterExit.OnTransitionExterior += OnTransitionToExterior;
            PlayerEnterExit.OnTransitionDungeonExterior += OnTransitionToDungeonExterior;
            PlayerEnterExit.OnTransitionInterior += OnTransitionToInterior;
            PlayerEnterExit.OnTransitionDungeonInterior += OnTransitionToDungeonInterior;
            SaveLoadManager.OnLoad += OnLoadEvent;
        }

        void OnDestroy()
        {
            // important that transition events/delegates are destroyed in OnDestroy() instead of OnDisable (since exteriorAutomap gameobject is disabled when going indoors and enabled when going outdoors)
            PlayerGPS.OnMapPixelChanged -= OnMapPixelChanged;
            PlayerEnterExit.OnTransitionExterior -= OnTransitionToExterior;
            PlayerEnterExit.OnTransitionDungeonExterior -= OnTransitionToDungeonExterior;
            PlayerEnterExit.OnTransitionInterior -= OnTransitionToInterior;
            PlayerEnterExit.OnTransitionDungeonInterior -= OnTransitionToDungeonInterior;
            SaveLoadManager.OnLoad -= OnLoadEvent;
        }

        /*
        void OnEnable()
        {

        }

        void OnDisable()
        {

        }

        void Start()
        {

        }
        */

        void Update()
        {
            if ((GameManager.Instance.StateManager.CurrentState == StateManager.StateTypes.UI) && cameraExteriorAutomap)            
            {
                DaggerfallUI daggerfallUI;
                DaggerfallUI.FindDaggerfallUI(out daggerfallUI);

                Panel panelExteriorAutomap = daggerfallUI.ExteriorAutomapWindow.PanelRenderAutomap;
                Vector2 mousePosition = new Vector2(panelExteriorAutomap.MousePosition.x - panelExteriorAutomap.Position.x, panelExteriorAutomap.InteriorHeight - 1 - (panelExteriorAutomap.MousePosition.y - panelExteriorAutomap.Position.y)); // Input.mousePosition

                Ray ray = cameraExteriorAutomap.ScreenPointToRay(mousePosition);
                //Debug.Log(String.Format("ox: {0}, oy: {1}, oz: {2}; dx: {3}, dy: {4}, dz: {5}", ray.origin.x, ray.origin.y, ray.origin.z, ray.direction.x, ray.direction.y, ray.direction.z));                
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit, layerAutomap))
                {
                    string currentNameplateName = "";
                    //Debug.Log(hit.collider.gameObject.name);
                    int lengthSkipStart = ("building name plate for [").Length;
                    int lengthSkipEnd = ("]").Length + 1; // +1 for the "+" resp "*" character at the end to indicate if nameplate was placed with name or "*" on the map
                    string gameObjectName = hit.collider.gameObject.name;
                    string nameplateName = gameObjectName.Substring(lengthSkipStart, gameObjectName.Length - lengthSkipStart - lengthSkipEnd);
                    
                    bool shortFormUsed = gameObjectName.Substring(gameObjectName.Length - 1, 1) == "*";
                    if (!shortFormUsed)
                        return;

                    if (popUpNameplate == null)
                    {
                        TextLabel textLabel = DaggerfallUI.AddTextLabel(customFont, Vector2.zero, nameplateName);
                        textLabel.BackgroundColor = Color.blue; // not working right now - always black background (have to look into this if I can make it work...)
                        textLabel.TextColor = Color.yellow;
                        popUpNameplate = new GameObject("pop-up nameplate");
                        MeshFilter meshFilter = (MeshFilter)popUpNameplate.AddComponent(typeof(MeshFilter));
                        meshFilter.mesh = CreateLeftAlignedMesh(textLabel.Texture.width, textLabel.Texture.height); // create left aligned (in relation to gameobject position) quad with normal facing into positive y-direction
                        MeshRenderer renderer = popUpNameplate.AddComponent(typeof(MeshRenderer)) as MeshRenderer;
                        renderer.material.shader = Shader.Find("Unlit/Texture");
                        renderer.material.mainTexture = textLabel.Texture;
                        renderer.enabled = true;

                        SetLayerRecursively(popUpNameplate, layerAutomap);
                        popUpNameplate.transform.SetParent(gameObjectBuildingNameplates.transform);

                        float scale = 0.5f;
                        popUpNameplate.transform.position = new Vector3(hit.point.x, nameplatesPlacementDepth + 2.0f, hit.point.z);
                        popUpNameplate.transform.rotation = hit.collider.gameObject.transform.rotation;
                        popUpNameplate.transform.localScale = new Vector3(scale, scale, scale);
                        
                        currentNameplateName = nameplateName;
                    }
                    else if (popUpNameplate && currentNameplateName != nameplateName)
                    {
                        TextLabel textLabel = DaggerfallUI.AddTextLabel(DaggerfallUI.DefaultFont, Vector2.zero, nameplateName);
                        MeshFilter meshFilter = popUpNameplate.GetComponent<MeshFilter>();
                        meshFilter.mesh = CreateLeftAlignedMesh(textLabel.Texture.width, textLabel.Texture.height); // create left aligned (in relation to gameobject position) quad with normal facing into positive y-direction                        
                        MeshRenderer renderer = popUpNameplate.GetComponent<MeshRenderer>();
                        renderer.material.mainTexture = textLabel.Texture;

                        popUpNameplate.transform.position = new Vector3(hit.point.x, nameplatesPlacementDepth + 2.0f, hit.point.z);
                        popUpNameplate.transform.rotation = hit.collider.gameObject.transform.rotation;
                        popUpNameplate.SetActive(true);

                        currentNameplateName = nameplateName;
                    }
                }
                else
                {
                    if (popUpNameplate != null)
                    {
                        popUpNameplate.SetActive(false);                        
                    }
                }

                daggerfallUI.ExteriorAutomapWindow.updateAutomapView();
                return;
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// sets layer of a GameObject and all of its childs recursively
        /// </summary>
        /// <param name="obj"> the target GameObject </param>
        /// <param name="layer"> the layer to be set </param>
        private static void SetLayerRecursively(GameObject obj, int layer)
        {
            obj.layer = layer;

            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        /// <summary>
        /// draw line with line renderer
        /// </summary>
        /// <param name="start"> start point of the line </param>
        /// <param name="end"> end point of the line </param>
        /// <param name="color"> color of the line </param>
        /// <param name="startWidth"> start width of the line </param>
        /// <param name="endWidth"> end width of the line </param>
        /// <returns> returns the GameObject that holds the line</returns>
        private GameObject DrawLine(Vector3 start, Vector3 end, Color color, float startWidth = 0.3f, float endWidth = 0.3f)
        {
            GameObject line = new GameObject();
            line.layer = layerAutomap;
            line.transform.position = start;
            line.AddComponent<LineRenderer>();
            LineRenderer lr = line.GetComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Unlit/Color"));
            lr.material.color = color;
            lr.startWidth = startWidth;
            lr.endWidth = endWidth;
            //lr.SetWidth(startWidth, endWidth);    // IK: Change to suppress warning
            lr.SetPosition(0, start);
            lr.SetPosition(1, end);
            return line;
        }

        /// <summary>
        /// creates the exterior automap camera if not present and sets camera default settings, registers camera to compass
        /// </summary>
        private void createExteriorAutomapCamera()
        {
            if (!cameraExteriorAutomap)
            {
                gameObjectCameraExteriorAutomap = new GameObject("CameraExteriorAutomap");
                cameraExteriorAutomap = gameObjectCameraExteriorAutomap.AddComponent<Camera>();
                cameraExteriorAutomap.clearFlags = CameraClearFlags.SolidColor;
                cameraExteriorAutomap.cullingMask = 1 << layerAutomap;
                cameraExteriorAutomap.renderingPath = Camera.main.renderingPath;
                cameraExteriorAutomap.orthographic = true;
                cameraExteriorAutomap.nearClipPlane = 0.7f;
                cameraExteriorAutomap.farClipPlane = 5000.0f;
                gameObjectCameraExteriorAutomap.transform.SetParent(gameobjectExteriorAutomap.transform);
            }
        }

        /// <summary>
        /// creates a mesh centered around the origin
        /// </summary>
        /// <param name="width"> width of the mesh to be created </param>
        /// <param name="height"> height of the mesh to be created </param>
        /// <returns> returns the newly created mesh </returns>
        private Mesh CreateMesh(float width, float height)
        {
            Mesh m = new Mesh();
            m.name = "ScriptedMesh";
            m.vertices = new Vector3[] {
                new Vector3(-width/2.0f, 0.01f, height/2.0f),
                new Vector3(width/2.0f, 0.01f, height/2.0f),
                new Vector3(width/2.0f, 0.01f, -height/2.0f),
                new Vector3(-width/2.0f, 0.01f, -height/2.0f)
            };
            m.uv = new Vector2[] {
                new Vector2 (0, 1),
                new Vector2 (1, 1),                
                new Vector2(1, 0),
                new Vector2 (0, 0)
            };
            m.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateNormals();

            return m;
        }

        /// <summary>
        /// creates a left-aligned mesh at the origin
        /// </summary>
        /// <param name="width"> width of the mesh to be created </param>
        /// <param name="height"> height of the mesh to be created </param>
        /// <returns> returns the newly created mesh </returns>
        private Mesh CreateLeftAlignedMesh(float width, float height)
        {
            Mesh m = new Mesh();
            m.name = "ScriptedMesh";
            m.vertices = new Vector3[] {
                new Vector3(0.0f, 0.01f, height/2.0f),
                new Vector3(width, 0.01f, height/2.0f),
                new Vector3(width, 0.01f, -height/2.0f),
                new Vector3(0.0f, 0.01f, -height/2.0f)
            };
            m.uv = new Vector2[] {
                new Vector2 (0, 1),
                new Vector2 (1, 1),                
                new Vector2(1, 0),
                new Vector2 (0, 0)
            };
            m.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateNormals();

            return m;
        }

        /// <summary>
        /// creates the quad that is used for exterior automap drawing
        /// </summary>        
        private void createCustomCanvasForExteriorAutomap()
        {
            if (gameobjectCustomCanvas != null)
            {
                UnityEngine.Object.Destroy(gameobjectCustomCanvas);
                gameobjectCustomCanvas = null;
            }

            gameobjectCustomCanvas = new GameObject("CustomCanvasExteriorAutomap");
            //gameobjectCustomCanvas.transform.rotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
            MeshFilter meshFilter = (MeshFilter)gameobjectCustomCanvas.AddComponent(typeof(MeshFilter));
            meshFilter.mesh = CreateMesh(layoutWidth * layoutMultiplier, layoutHeight * layoutMultiplier); // create quad with normal facing into positive y-direction
            MeshRenderer renderer = gameobjectCustomCanvas.AddComponent(typeof(MeshRenderer)) as MeshRenderer;

            renderer.material.shader = Shader.Find("Unlit/Transparent");
            //Texture2D tex = new Texture2D(1, 1);
            //tex.SetPixel(0, 0, Color.magenta);
            //tex.Apply();
            renderer.material.mainTexture = exteriorLayoutTexture;
            //renderer.material.color = Color.green;
            renderer.enabled = true;

            SetLayerRecursively(gameobjectCustomCanvas, layerAutomap);
            gameobjectCustomCanvas.transform.SetParent(gameobjectExteriorAutomap.transform);
        }

        private void assignExteriorLayoutTextureToCustomCanvas()
        {
            if (gameobjectCustomCanvas != null)
            {
                MeshRenderer renderer = gameobjectCustomCanvas.GetComponent<MeshRenderer>();
                renderer.material.mainTexture = exteriorLayoutTexture;
            }
        }

        private void createBuildingNameplates(DFLocation location)
        {
            deleteBuildingNameplates();

            List<BuildingNameplate> buildingNameplatesList = new List<BuildingNameplate>();

            gameObjectBuildingNameplates = new GameObject("building name plates");
            gameObjectBuildingNameplates.transform.SetParent(gameobjectExteriorAutomap.transform);

            DFBlock[] blocks;
            RMBLayout.GetLocationBuildingData(location, out blocks);
            int width = location.Exterior.ExteriorData.Width;
            int height = location.Exterior.ExteriorData.Height;            
          
            int uniqueIndex = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    BuildingSummary[] buildingsInBlock = RMBLayout.GetBuildingData(blocks[index], x, y);
                    BlockLayout layout = exteriorLayout[index];

                    foreach (BuildingSummary buildingSummary in buildingsInBlock)
                    {
                        //Debug.Log(String.Format("x: {0}, y: {1}", buildingSummary.Position.x, buildingSummary.Position.z));
                        int xPosBuilding = layout.rect.xpos + (int)(buildingSummary.Position.x / (BlocksFile.RMBDimension * MeshReader.GlobalScale) * blockSizeWidth);
                        int yPosBuilding = layout.rect.ypos + (int)(buildingSummary.Position.z / (BlocksFile.RMBDimension * MeshReader.GlobalScale) * blockSizeHeight);

                        BuildingNameplate newBuildingNameplate;
                        try
                        {
                            newBuildingNameplate.name = ""; // default value for player has not discovered location or building yet

                            // now check if building has been discovered already
                            PlayerGPS.DiscoveredBuilding discoveredBuilding;
                            if (GameManager.Instance.PlayerGPS.GetDiscoveredBuilding(buildingSummary.buildingKey, out discoveredBuilding))
                            {
                                // only create nameplates for buildings different than "Residence"
                                if (discoveredBuilding.displayName != "Residence")
                                    newBuildingNameplate.name = discoveredBuilding.displayName;
                            }
                            else if (this.revealUndiscoveredBuildings)
                            {
                                newBuildingNameplate.name = BuildingNames.GetName(buildingSummary.NameSeed, buildingSummary.BuildingType, buildingSummary.FactionId, location.Name, location.RegionName);
                            }                  
                        }
                        catch (Exception e)
                        {
                            string exceptionMessage = String.Format("exception occured in function BuildingNames.GetName (exception message: " + e.Message + @") with params: 
                                                                        seed: {0}, type: {1}, factionID: {2}, locationName: {3}, regionName: {4}",
                                                                        buildingSummary.NameSeed, buildingSummary.BuildingType, buildingSummary.FactionId, location.Name, location.RegionName);
                            DaggerfallUnity.LogMessage(exceptionMessage, true);

                            //fallback
                            switch (buildingSummary.BuildingType)
                            {
                                case DFLocation.BuildingTypes.Alchemist:
                                    newBuildingNameplate.name = "Alchemist";
                                    break;
                                case DFLocation.BuildingTypes.Armorer:
                                    newBuildingNameplate.name = "Armorer";
                                    break;
                                case DFLocation.BuildingTypes.Bank:
                                    newBuildingNameplate.name = "Bank";
                                    break;
                                case DFLocation.BuildingTypes.Bookseller:
                                    newBuildingNameplate.name = "Bookseller";
                                    break;
                                case DFLocation.BuildingTypes.ClothingStore:
                                    newBuildingNameplate.name = "Clothing Store";
                                    break;
                                case DFLocation.BuildingTypes.FurnitureStore:
                                    newBuildingNameplate.name = "Furniture Store";
                                    break;
                                case DFLocation.BuildingTypes.GemStore:
                                    newBuildingNameplate.name = "Gem Store";
                                    break;
                                case DFLocation.BuildingTypes.GeneralStore:
                                    newBuildingNameplate.name = "General Store";
                                    break;
                                case DFLocation.BuildingTypes.GuildHall:
                                    newBuildingNameplate.name = "Guild Hall";
                                    break;
                                case DFLocation.BuildingTypes.HouseForSale:
                                    newBuildingNameplate.name = "House for Sale";
                                    break;
                                case DFLocation.BuildingTypes.Library:
                                    newBuildingNameplate.name = "Library";
                                    break;
                                case DFLocation.BuildingTypes.Palace:
                                    newBuildingNameplate.name = "Palace";
                                    break;
                                case DFLocation.BuildingTypes.PawnShop:
                                    newBuildingNameplate.name = "Pawn Shop";
                                    break;
                                case DFLocation.BuildingTypes.Tavern:
                                    newBuildingNameplate.name = "Tavern";
                                    break;
                                case DFLocation.BuildingTypes.Temple:
                                    newBuildingNameplate.name = "Temple";
                                    break;
                                case DFLocation.BuildingTypes.WeaponSmith:
                                    newBuildingNameplate.name = "Weapon Smith";
                                    break;
                                default:
                                    newBuildingNameplate.name = "unknown";
                                    break;
                            }
                        }

                        if (newBuildingNameplate.name != "")
                        {
                            newBuildingNameplate.uniqueIndex = uniqueIndex++;
                            newBuildingNameplate.anchorPoint.x = xPosBuilding;
                            newBuildingNameplate.anchorPoint.y = yPosBuilding;
                            newBuildingNameplate.textLabel = DaggerfallUI.AddTextLabel(customFont, Vector2.zero, newBuildingNameplate.name);
                            newBuildingNameplate.gameObject = new GameObject(String.Format("building name plate for [{0}]+", newBuildingNameplate.name));
                            MeshFilter meshFilter = (MeshFilter)newBuildingNameplate.gameObject.AddComponent(typeof(MeshFilter));
                            meshFilter.mesh = CreateLeftAlignedMesh(newBuildingNameplate.textLabel.Texture.width, newBuildingNameplate.textLabel.Texture.height); // create left aligned (in relation to gameobject position) quad with normal facing into positive y-direction
                            MeshRenderer renderer = newBuildingNameplate.gameObject.AddComponent(typeof(MeshRenderer)) as MeshRenderer;

                            MeshCollider collider = newBuildingNameplate.gameObject.AddComponent<MeshCollider>();
                            if (collider)
                            {
                                // Doing nothing just to suppress warning
                            }

                            renderer.material.shader = Shader.Find("Unlit/Transparent");
                            renderer.material.mainTexture = newBuildingNameplate.textLabel.Texture;
                            renderer.enabled = true;

                            SetLayerRecursively(newBuildingNameplate.gameObject, layerAutomap);
                            newBuildingNameplate.gameObject.transform.SetParent(gameObjectBuildingNameplates.transform);

                            float posX = newBuildingNameplate.anchorPoint.x - locationWidth * blockSizeWidth * 0.5f;
                            float posY = newBuildingNameplate.anchorPoint.y - locationHeight * blockSizeHeight * 0.5f;                            
                            newBuildingNameplate.scale = 0.5f;
                            newBuildingNameplate.width = newBuildingNameplate.textLabel.Texture.width * newBuildingNameplate.scale;
                            newBuildingNameplate.height = newBuildingNameplate.textLabel.Texture.height * newBuildingNameplate.scale;
                            newBuildingNameplate.gameObject.transform.position = new Vector3(posX, nameplatesPlacementDepth, posY);
                            newBuildingNameplate.gameObject.transform.localScale = new Vector3(newBuildingNameplate.scale, newBuildingNameplate.scale, newBuildingNameplate.scale);
                            newBuildingNameplate.offset = Vector2.zero;
                            newBuildingNameplate.upperLeftCorner = new Vector2(0.0f, +newBuildingNameplate.height * 0.5f);
                            newBuildingNameplate.upperRightCorner = new Vector2(newBuildingNameplate.width, +newBuildingNameplate.height * 0.5f);
                            newBuildingNameplate.lowerLeftCorner = new Vector2(0.0f, -newBuildingNameplate.height * 0.5f);
                            newBuildingNameplate.lowerRightCorner = new Vector2(newBuildingNameplate.width, -newBuildingNameplate.height * 0.5f);
                            newBuildingNameplate.placed = false;
                            newBuildingNameplate.nameplateReplaced = false;
                            newBuildingNameplate.numCollisionsDetected = 0;                            

                            #if DEBUG_Nameplates
                            newBuildingNameplate.anchorLine = null;
                            newBuildingNameplate.debugLine1 = null;
                            newBuildingNameplate.debugLine2 = null;
                            #endif

                            buildingNameplatesList.Add(newBuildingNameplate);
                        }
                    }
                }
            }

            // convert the list of building nameplates to an array for performance reasons
            buildingNameplates = new BuildingNameplate[buildingNameplatesList.Count];
            for (int i = 0; i < buildingNameplatesList.Count; i++)
            {
                buildingNameplates[i] = buildingNameplatesList[i];
            }
            buildingNameplatesList.Clear();            

            computeNameplateOffsets();
            applyNameplateOffsets();
        }

        private void deleteBuildingNameplates()
        {
            if (buildingNameplates != null)
            {
                foreach (BuildingNameplate n in buildingNameplates)
                {
                    if (n.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(n.gameObject);
                    }
                }
                buildingNameplates = null;
            }

            if (gameObjectBuildingNameplates != null)
            {
                UnityEngine.Object.Destroy(gameObjectBuildingNameplates);
                gameObjectBuildingNameplates = null;
            }
        }

        /// <summary>
        /// this function checks for intersection (collisions) of nameplates with given offsets and returns the direction vector and shift amounts that
        /// are needed to correct the nameplates positions to make them no longer intersect/collide.
        /// former computed offsets of nameplates are taken into account (offsets will only be != Vector2.zero if the corresponding nameplate has been placed)!
        /// It does not actually change/correct positions! It only computes the necessary values that are needed for a consecutive correction step.
        /// </summary>
        /// <param name="nameplate1"> the first nameplate as part of the check </param>
        /// <param name="tryOffset1"> offset in world coordinates for the first nameplate to be tried/tested (without a former computed nameplate offset, since this is taken into account automatically) </param>
        /// <param name="nameplate2"> the second nameplate as part of the check </param>
        /// <param name="tryOffset2"> offset in world coordinates for the second nameplate to be tried/tested (without a former computed nameplate offset, since this is taken into account automatically) </param>
        /// <param name="vectorNameplate1VerticalOffset"> the computed output offset direction in world coordinates for the first nameplate (which is a vertical displacement in local nameplate coordinate space) </param>
        /// <param name="vectorNameplate2VerticalOffset"> the computed output offset direction in world coordinates for the second nameplate (which is a vertical displacement in local nameplate coordinate space) </param>
        /// <param name="ySize"> the computed y size (vertical in local nameplate coordinate space) that needs to be between nameplates' anchor points so that no collision happens between the two </param>
        /// <param name="distanceVertical"> the distance (vertical in local nameplate coordinate space) that needs to be added to make both nameplates not collide </param>
        /// <returns> returns true if nameplates with given offsets intersect/collide, otherwise false </returns>
        private bool checkIntersectionOfNameplates(BuildingNameplate nameplate1, Vector2 tryOffset1, BuildingNameplate nameplate2, Vector2 tryOffset2, out Vector2 vectorNameplate1VerticalOffset, out Vector2 vectorNameplate2VerticalOffset, out float ySize, out float distanceVertical)
        {        
            Vector2 vectorBetweenNamePlates = (new Vector2(nameplate2.gameObject.transform.position.x, nameplate2.gameObject.transform.position.z) + nameplate2.offset + tryOffset2) - (new Vector2(nameplate1.gameObject.transform.position.x, nameplate1.gameObject.transform.position.z) + nameplate1.offset + tryOffset1);

            Vector2 p;
            Vector2 posNameplate1 = Vector2.zero;
            Vector2 posNameplate2 = vectorBetweenNamePlates;

            Vector2 b = posNameplate1 + (nameplate1.upperRightCorner - nameplate1.upperLeftCorner);
            b.Normalize();
            Vector2 a = posNameplate2 - posNameplate1;
            float a1 = Vector2.Dot(a, b); // length of projected vector a onto b
            p = posNameplate1 + b * a1;
            distanceVertical = Vector2.Distance(posNameplate2, p);
            
            float xSize = Vector2.Distance(nameplate1.upperRightCorner, nameplate1.upperLeftCorner); // assume that first nameplate is "left" of second (in terms of the rotated coordinate system)
            // test if second nameplate is "left" and our initial assumption is false
            Vector2 pointRightNamePlate1 = (nameplate1.upperRightCorner + nameplate1.lowerRightCorner) * 0.5f;
            Vector2 u = pointRightNamePlate1 /* - Vector2.zero */; // direction vector of line
            float s = (p.x - 0.0f) / u.x;
            if (s < 0)
            {
                xSize = Vector2.Distance(nameplate2.upperRightCorner, nameplate2.upperLeftCorner);
            }

            ySize = Vector2.Distance(nameplate1.lowerLeftCorner, nameplate1.upperLeftCorner) * 0.5f + Vector2.Distance(nameplate2.lowerLeftCorner, nameplate2.upperLeftCorner) * 0.5f;

            // test if (rotated) nameplates intersect
            bool intersect = distanceVertical < ySize;
            float distanceHorizontal = Vector2.Distance(posNameplate1, p);
            intersect &= distanceHorizontal < xSize;

            Vector2 halfPoint = (posNameplate2 + p) * 0.5f; // point lying halfway between centerNameplate2 and p
            vectorNameplate1VerticalOffset = (p - halfPoint).normalized;
            vectorNameplate2VerticalOffset = (posNameplate2 - halfPoint).normalized;
            if (vectorNameplate1VerticalOffset == Vector2.zero)
            {
                vectorNameplate1VerticalOffset = Vector2.up;
            }
            if (vectorNameplate2VerticalOffset == Vector2.zero)
            {
                vectorNameplate2VerticalOffset = Vector2.down;
            }

            return intersect;
        }

        /// <summary>
        /// this function checks for intersection (collisions) of nameplates with given offsets
        /// </summary>
        /// <param name="nameplate1"> the first nameplate as part of the check </param>
        /// <param name="tryOffset1"> offset in world coordinates for the first nameplate to be tried/tested (without a former computed nameplate offset, since this is taken into account automatically) </param>
        /// <param name="nameplate2"> the second nameplate as part of the check </param>
        /// <param name="tryOffset2"> offset in world coordinates for the second nameplate to be tried/tested (without a former computed nameplate offset, since this is taken into account automatically) </param>
        /// <returns> returns true if nameplates with given offsets intersect/collide, otherwise false </returns>
        private bool checkIntersectionOfNameplates(BuildingNameplate nameplate1, Vector2 tryOffset1, BuildingNameplate nameplate2, Vector2 tryOffset2)
        {
            Vector2 vectorNameplate1VerticalOffset, vectorNameplate2VerticalOffset;
            float ySize, distanceVertical;
            return (checkIntersectionOfNameplates(nameplate1, tryOffset1, nameplate2, tryOffset2, out vectorNameplate1VerticalOffset, out vectorNameplate2VerticalOffset, out ySize, out distanceVertical));
        }

        /// <summary>
        /// this function checks for intersection (collisions) of a given nameplate with a given offset against all other nameplates in array of nameplates with their current positions corrected by already applied offsets
        /// </summary>
        /// <param name="nameplate"> the nameplate to be checked </param>
        /// <param name="tryOffset"> offset in world coordinates for the nameplate to be tried/tested (without a former computed nameplate offset, since this is taken into account automatically) </param>
        /// <param name="onlyCheckPlaced"> if true only already placed nameplates are considered in this check </param>
        /// <param name="onlyCheckUnplaced"> if true only unplaced nameplates are considered in this check </param>
        /// <param name="skipNameplate"> an optional nameplate that can be skipped in the check </param>
        /// <returns> returns true if the nameplate does not collide on the test position </returns>
        private bool checkIntersectionOffsetNameplateAgainstOthers(BuildingNameplate nameplate, Vector2 tryOffset, bool onlyCheckPlaced = false, bool onlyCheckUnplaced = false, BuildingNameplate? skipNameplate = null)
        {
            bool check = false;
            for (int i=0; i < buildingNameplates.Length; i++)
            {
                BuildingNameplate otherNameplate = buildingNameplates[i];
                if ((skipNameplate.HasValue) && (skipNameplate.Value.uniqueIndex == otherNameplate.uniqueIndex))
                    continue;
                if (nameplate.uniqueIndex == otherNameplate.uniqueIndex)
                    continue;
                if (onlyCheckPlaced && !otherNameplate.placed)
                    continue;
                if (onlyCheckUnplaced && otherNameplate.placed)
                    continue;
                check |= checkIntersectionOfNameplates(nameplate, tryOffset, otherNameplate, Vector2.zero);
                if (check)
                    break;                
            }
            return check;
        }

        /// <summary>
        /// this functions counts the number of collisions for a given nameplate with a given offset
        /// </summary>
        /// <param name="nameplate"> the nameplate to be checked </param>
        /// <param name="tryOffset"> offset in world coordinates for the nameplate to be tried/tested (without a former computed nameplate offset, since this is taken into account automatically) </param>
        /// <param name="onlyCountPlaced"> if true only already placed nameplates are taken into account when counting </param>
        /// <param name="onlyCountUnplaced"> if true only unplaced nameplates are taken into account when counting </param>
        /// <returns> the number of collisions counted </returns>
        private int numberOfCollisionsNameplatesWithOffsetNameplate(BuildingNameplate nameplate, Vector2 tryOffset, bool onlyCountPlaced, bool onlyCountUnplaced = false)
        {
            int numCollisions = 0;
            for (int i = 0; i < buildingNameplates.Length; i++)
            {
                BuildingNameplate otherNameplate = buildingNameplates[i];
                if (nameplate.uniqueIndex == otherNameplate.uniqueIndex)
                    continue;
                if (onlyCountPlaced && !otherNameplate.placed)
                    continue;
                if (onlyCountUnplaced && otherNameplate.placed)
                    continue;
                if (checkIntersectionOfNameplates(nameplate, tryOffset, otherNameplate, Vector2.zero))
                {
                    numCollisions++;
                }
            }
            return numCollisions;
        }

        /// <summary>
        /// helper function to (re-)compute number of collisions and place those nameplates that don't have any collisions with other nameplates
        /// note: since it is expensive to compute collision for all nameplates this should only be done when necessary (use parameter recomputeNumberOfCollision to enable or disable recomputing)
        /// </summary>
        /// <param name="recomputeNumberOfCollision"> if true recomputes the number of collisions </param>
        private void computeAndPlaceZeroCollisionsNameplates(bool recomputeNumberOfCollision = true)
        {
            // compute number of collisions for every nameplate and directly place those with numCollisionsDetected == 0
            for (int i = 0; i < buildingNameplates.Length; i++)
            {
                BuildingNameplate buildingNameplate = buildingNameplates[i];
                if (recomputeNumberOfCollision)
                {
                    buildingNameplate.numCollisionsDetected = numberOfCollisionsNameplatesWithOffsetNameplate(buildingNameplate, Vector2.zero, false, false);
                }
                if (buildingNameplate.numCollisionsDetected == 0)
                {
                    buildingNameplate.placed = true; // place it in current position since there is no collision
                }
                // store changes to nameplates array
                buildingNameplates[i] = buildingNameplate;
            }
                        
            if (allowRightAlignedNameplates)
            {
                // compute number of collisions for right aligned nameplates and directly place those with numCollisionsDetected == 0
                for (int i = 0; i < buildingNameplates.Length; i++)
                {
                    BuildingNameplate buildingNameplate = buildingNameplates[i];
                    if (buildingNameplate.placed)
                        continue;
                    Vector2 vectorNormalBiasNameplate = (buildingNameplate.upperLeftCorner - buildingNameplate.upperRightCorner);
                    buildingNameplate.numCollisionsDetected = numberOfCollisionsNameplatesWithOffsetNameplate(buildingNameplate, vectorNormalBiasNameplate, false, false);
                    if (buildingNameplate.numCollisionsDetected == 0)
                    {
                        buildingNameplate.offset = vectorNormalBiasNameplate;
                        buildingNameplate.placed = true; // place it in current position since there is no collision
                    }
                    // store changes to nameplates array
                    buildingNameplates[i] = buildingNameplate;
                }
            }
        }

        /// <summary>
        /// this is the main function for computing offsets of nameplates
        /// </summary>
        private void computeNameplateOffsets()
        {
            for (int t = 0; t < 3; t++) // main loop max n times (then everything should either be solved or is considered unsolveable)
            {
                // compute number of collisions for every nameplate and directly place those with numCollisionsDetected == 0
                computeAndPlaceZeroCollisionsNameplates(t == 0); // only compute number of collisions for nameplates on first loop iteration, afterwards it is just updated in-place (performance reasons)

                for (int i = 0; i < buildingNameplates.Length; i++)
                {
                    BuildingNameplate first = buildingNameplates[i];
                    if (first.placed) // if already placed skip nameplate and continue with next nameplate
                        continue;

                    Vector2 vectorNameplate1VerticalOffset = Vector2.zero, vectorNameplate2VerticalOffset = Vector2.zero;
                    float ySize = 0.0f;
                    float distanceVertical = 0.0f;

                    if (first.numCollisionsDetected == 1) // only "easy" cases (with just one collision) are handled in main loop (on every loop iteration previously "hard" cases could have become "easy" ones)
                    {
                        // find colliding nameplate
                        int j = 0;
                        for (j = 0; j < buildingNameplates.Length; j++)
                        {
                            BuildingNameplate otherBuildingNameplate = buildingNameplates[j];
                            if (first.uniqueIndex == otherBuildingNameplate.uniqueIndex)
                                continue;
                            if (otherBuildingNameplate.placed) // don't check for already placed nameplates for collisions - they are fixed and are only used for checks when trying to place unplaced nameplates
                                continue;
                            if (checkIntersectionOfNameplates(first, Vector2.zero, otherBuildingNameplate, Vector2.zero, out vectorNameplate1VerticalOffset, out vectorNameplate2VerticalOffset, out ySize, out distanceVertical))
                                break; // if found break loop
                        }

                        if (j >= buildingNameplates.Length) // if no colliding nameplate was found: internal state of numCollisionsDetected == 1 was somehow wrong (actually this if should never be reached, but who knows...)
                        {
                            first.numCollisionsDetected = 0;
                            first.placed = true; // place it in current position since there is no collision
                            continue;
                        }

                        // get nameplate that collided with first (and was the only colliding nameplate since we tested first.numCollisionsDetected == 1 before
                        BuildingNameplate second = buildingNameplates[j];

                        if (second.numCollisionsDetected == 1) // easy case, second nameplate (colliding nameplate has only one collision (which must be the first nameplate))
                        {
                            Vector2 vectorBiasNameplate1 = vectorNameplate1VerticalOffset * (ySize - Math.Abs(distanceVertical)) * 0.5f;
                            Vector2 vectorBiasNameplate2 = vectorNameplate2VerticalOffset * (ySize - Math.Abs(distanceVertical)) * 0.5f;

                            Vector2 vectorNormalBiasNameplate1 = (first.upperLeftCorner - first.upperRightCorner);
                            Vector2 vectorNormalBiasNameplate2 = (second.upperLeftCorner - second.upperRightCorner);
                            //string stringNameplate1 = "";
                            //string stringNameplate2 = "";

                            // try to place both nameplates by shifting half amount to up/down - but check if they then don't collide with already placed nameplates
                            if (!checkIntersectionOffsetNameplateAgainstOthers(first, vectorBiasNameplate1, true, false, second) && !checkIntersectionOffsetNameplateAgainstOthers(second, vectorBiasNameplate2, true, false, first))
                            {
                                first.offset = vectorBiasNameplate1;
                                first.placed = true;
                                //stringNameplate1 = String.Format("{0} {1}", first.name, "^");

                                second.offset = vectorBiasNameplate2;
                                second.placed = true;
                                //stringNameplate2 = String.Format("{0} {1}", second.name, "v");
                            }
                            // if previous placement was not possible, try same with right-alligned first nameplate (if right-alligned nameplates are allowed)
                            else if (allowRightAlignedNameplates && !checkIntersectionOffsetNameplateAgainstOthers(first, vectorBiasNameplate1 + vectorNormalBiasNameplate1, true, false, second) && !checkIntersectionOffsetNameplateAgainstOthers(second, vectorBiasNameplate2, true, false, first))
                            {
                                first.offset = vectorBiasNameplate1 + vectorNormalBiasNameplate1;
                                first.placed = true;

                                second.offset = vectorBiasNameplate2;
                                second.placed = true;
                            }
                            // if previous placement was not possible, try same with right-alligned second nameplate (if right-alligned nameplates are allowed)
                            else if (allowRightAlignedNameplates && !checkIntersectionOffsetNameplateAgainstOthers(first, vectorBiasNameplate1, true, false, second) && !checkIntersectionOffsetNameplateAgainstOthers(second, vectorBiasNameplate2 + vectorNormalBiasNameplate2, true, false, first))
                            {
                                first.offset = vectorBiasNameplate1;
                                first.placed = true;

                                second.offset = vectorBiasNameplate2 + vectorNormalBiasNameplate2;
                                second.placed = true;
                            }
                            // if previous placement was not possible, try to place first (and maybe second) nameplate by shifting first the required amount away from second - but check if first doesn't collide with already placed nameplates
                            else if (!checkIntersectionOffsetNameplateAgainstOthers(first, vectorBiasNameplate1 * 2.0f, true, false))
                            {
                                first.offset = vectorBiasNameplate1 * 2.0f;
                                first.placed = true;
                                buildingNameplates[i] = first; // important so next check can succeed
                                // then check if second doesn't collide with already placed nameplates if it is placed at its current place
                                if (!checkIntersectionOffsetNameplateAgainstOthers(second, Vector2.zero, true, false))
                                    second.placed = true;
                                //stringNameplate1 = String.Format("{0} {1}", first.name, "_^");
                                //stringNameplate2 = String.Format("{0} {1}", second.name, "!");
                            }
                            // if previous placement was not possible, try to place second (and maybe first) nameplate by shifting second the required amount away from first - but check if second doesn't collide with already placed nameplates
                            else if (!checkIntersectionOffsetNameplateAgainstOthers(second, vectorBiasNameplate2 * 2.0f, true, false))
                            {
                                second.offset = vectorBiasNameplate2 * 2.0f;
                                second.placed = true;
                                buildingNameplates[j] = second; // important so next check can succeed
                                // then check if first doesn't collide with already placed nameplates if it is placed at its current place
                                if (!checkIntersectionOffsetNameplateAgainstOthers(first, Vector2.zero, true, false))
                                    first.placed = true;
                                //stringNameplate1 = String.Format("{0} {1}", first.name, "_v");
                                //stringNameplate2 = String.Format("{0} {1}", second.name, "!");
                            }
                            // if previous placement was not possible, try to place first (and maybe second) nameplate by right-aligning (if allowed) and shifting first the required amount away from second - but check if first doesn't collide with already placed nameplates
                            else if (allowRightAlignedNameplates && !checkIntersectionOffsetNameplateAgainstOthers(first, vectorBiasNameplate1 * 2.0f + vectorNormalBiasNameplate1, true, false))
                            {
                                first.offset = vectorBiasNameplate1 * 2.0f + vectorNormalBiasNameplate1;
                                first.placed = true;
                                buildingNameplates[i] = first; // important so next check can succeed
                                // then check if second doesn't collide with already placed nameplates if it is placed at its current place
                                if (!checkIntersectionOffsetNameplateAgainstOthers(second, Vector2.zero, true, false))
                                    second.placed = true;
                            }
                            // if previous placement was not possible, try to place second (and maybe first) nameplate by right-aligning (if allowed) and shifting second the required amount away from first - but check if second doesn't collide with already placed nameplates
                            else if (allowRightAlignedNameplates && !checkIntersectionOffsetNameplateAgainstOthers(second, vectorBiasNameplate2 * 2.0f + vectorNormalBiasNameplate2, true, false))
                            {
                                second.offset = vectorBiasNameplate2 * 2.0f + vectorNormalBiasNameplate2;
                                second.placed = true;
                                buildingNameplates[j] = second; // important so next check can succeed
                                // then check if first doesn't collide with already placed nameplates if it is placed at its current place
                                if (!checkIntersectionOffsetNameplateAgainstOthers(first, Vector2.zero, true, false))
                                    first.placed = true;
                            }

                            //if (stringNameplate1 != "")
                            //{
                            //    TextLabel newTextLabel = DaggerfallUI.AddTextLabel(DaggerfallUI.DefaultFont, Vector2.zero, stringNameplate1);
                            //    first.textLabel = newTextLabel;
                            //    MeshRenderer renderer = first.gameObject.GetComponent<MeshRenderer>();
                            //    renderer.material.mainTexture = newTextLabel.Texture;
                            //    //buildingNameplates[i] = first;
                            //}
                            //if (stringNameplate2 != "")
                            //{
                            //    TextLabel newTextLabel = DaggerfallUI.AddTextLabel(DaggerfallUI.DefaultFont, Vector2.zero, stringNameplate2);
                            //    second.textLabel = newTextLabel;
                            //    MeshRenderer renderer = second.gameObject.GetComponent<MeshRenderer>();
                            //    renderer.material.mainTexture = newTextLabel.Texture;
                            //    //buildingNameplates[j] = second;
                            //}

                        }
                        else if (second.numCollisionsDetected > 1) // "trickier" case, second nameplate has multiple collisions -> only offset first nameplate then...
                        {
                            Vector2 vectorBiasNameplate = vectorNameplate1VerticalOffset * (ySize);
                            Vector2 vectorNormalBiasNameplate = (first.upperLeftCorner - first.upperRightCorner);

                            //string stringNameplate1 = "";

                            if (!checkIntersectionOffsetNameplateAgainstOthers(first, vectorBiasNameplate, true, false))
                            {
                                first.offset = vectorBiasNameplate;
                                first.placed = true;
                                //stringNameplate1 = String.Format("{0} {1}", first.name, "__^");
                            }
                            else if (!checkIntersectionOffsetNameplateAgainstOthers(first, -vectorBiasNameplate, true, false))
                            {
                                first.offset = -vectorBiasNameplate;
                                first.placed = true;
                                //stringNameplate1 = String.Format("{0} {1}", first.name, "__v");
                            }
                            else if (allowRightAlignedNameplates && !checkIntersectionOffsetNameplateAgainstOthers(first, vectorBiasNameplate + vectorNormalBiasNameplate, true, false))
                            {
                                first.offset = vectorBiasNameplate + vectorNormalBiasNameplate;
                                first.placed = true;
                            }
                            else if (allowRightAlignedNameplates && !checkIntersectionOffsetNameplateAgainstOthers(first, -vectorBiasNameplate + vectorNormalBiasNameplate, true, false))
                            {
                                first.offset = -vectorBiasNameplate + vectorNormalBiasNameplate;
                                first.placed = true;
                            }

                            if (first.placed)
                                second.numCollisionsDetected--;

                            //if (stringNameplate1 != "")
                            //{
                            //    TextLabel newTextLabel = DaggerfallUI.AddTextLabel(DaggerfallUI.DefaultFont, Vector2.zero, stringNameplate1);
                            //    first.textLabel = newTextLabel;
                            //    MeshRenderer renderer = first.gameObject.GetComponent<MeshRenderer>();
                            //    renderer.material.mainTexture = newTextLabel.Texture;
                            //    //buildingNameplates[i] = first;
                            //}
                        }

                        // store changes to nameplates array
                        buildingNameplates[i] = first;
                        buildingNameplates[j] = second;
                    }
                }

                // place those with numCollisionsDetected == 0, don't recompute (performance reasons) since they should be correct anyway
                computeAndPlaceZeroCollisionsNameplates(false);

                // now try to place remaining nameplates (all nameplates with more than 1 collisions)
                for (int i = 0; i < buildingNameplates.Length; i++)
                {
                    BuildingNameplate first = buildingNameplates[i];
                    //if (first.numCollisionsDetected <= 1)
                    //    continue;
                    if (first.placed)
                        continue;
                    Vector2 vectorDirectionNameplate = (first.upperLeftCorner - first.lowerLeftCorner).normalized;
                    Vector2 vectorBiasNameplate = vectorDirectionNameplate * (first.height);
                    Vector2 vectorNormalBiasNameplate = (first.upperLeftCorner - first.upperRightCorner);

                    //string stringNameplate1 = "";

                    if (!checkIntersectionOffsetNameplateAgainstOthers(first, vectorBiasNameplate, true, false))
                    {
                        first.offset += vectorBiasNameplate;
                        first.placed = true;
                        //stringNameplate1 = String.Format("{0} {1}", first.name, "___^");
                    }
                    else if (!checkIntersectionOffsetNameplateAgainstOthers(first, -vectorBiasNameplate, true, false))
                    {
                        first.offset -= vectorBiasNameplate;
                        first.placed = true;
                        //stringNameplate1 = String.Format("{0} {1}", first.name, "___v");
                    }
                    else if (allowRightAlignedNameplates && !checkIntersectionOffsetNameplateAgainstOthers(first, vectorBiasNameplate + vectorNormalBiasNameplate, true, false))
                    {
                        first.offset = vectorBiasNameplate + vectorNormalBiasNameplate;
                        first.placed = true;
                    }
                    else if (allowRightAlignedNameplates && !checkIntersectionOffsetNameplateAgainstOthers(first, -vectorBiasNameplate + vectorNormalBiasNameplate, true, false))
                    {
                        first.offset = -vectorBiasNameplate + vectorNormalBiasNameplate;
                        first.placed = true;
                    }

                    //TextLabel newTextLabel = DaggerfallUI.AddTextLabel(DaggerfallUI.DefaultFont, Vector2.zero, stringNameplate1);
                    //first.textLabel = newTextLabel;
                    //MeshRenderer renderer = first.gameObject.GetComponent<MeshRenderer>();
                    //renderer.material.mainTexture = newTextLabel.Texture;

                    // store changes to nameplates array
                    buildingNameplates[i] = first;
                }
            } // for (int t = 0; t < 3; t++)

            // final pass to check if now some nameplates can be set that no longer have collisions, don't recompute number of collisions (performance reasons) since they should be correct anyway
            computeAndPlaceZeroCollisionsNameplates(false);

            // now place all remaining nameplates (that could not be placed without collisions) as "*" nameplates
            for (int i = 0; i < buildingNameplates.Length; i++)
            {
                BuildingNameplate buildingNameplate = buildingNameplates[i];
                if (!buildingNameplate.placed)
                {        
                    buildingNameplate.placed = true;

                    TextLabel newTextLabel = DaggerfallUI.AddTextLabel(customFont, Vector2.zero, "*");
                    buildingNameplate.textLabel = newTextLabel;
                    //MeshRenderer renderer = buildingNameplate.gameObject.GetComponent<MeshRenderer>();                    
                    MeshFilter meshFilter = buildingNameplate.gameObject.GetComponent<MeshFilter>();
                    meshFilter.mesh = CreateLeftAlignedMesh(buildingNameplate.textLabel.Texture.width, buildingNameplate.textLabel.Texture.height); // create left aligned (in relation to gameobject position) quad with normal facing into positive y-direction
                    MeshCollider meshCollider = buildingNameplate.gameObject.GetComponent<MeshCollider>(); 
                    meshCollider.sharedMesh = meshFilter.mesh;
                    MeshRenderer renderer = buildingNameplate.gameObject.GetComponent<MeshRenderer>();
                    renderer.material.mainTexture = newTextLabel.Texture;

                    buildingNameplate.gameObject.name = buildingNameplate.gameObject.name.Substring(0, buildingNameplate.gameObject.name.Length - 1) + "*";
                    buildingNameplate.gameObject.transform.position = new Vector3(buildingNameplate.gameObject.transform.position.x, nameplatesPlacementDepth + 1.0f, buildingNameplate.gameObject.transform.position.z); // set a bit higher than other nameplates so that it will get mouse over pop-up
                    buildingNameplate.nameplateReplaced = true;
                }
                // store changes to nameplates array
                buildingNameplates[i] = buildingNameplate;
            }
        }

        private void applyNameplateOffsets()
        {
            for (int i = 0; i < buildingNameplates.Length; i++)
            {
                BuildingNameplate buildingNameplate = buildingNameplates[i];
                //buildingNameplate.gameObject.transform.position.Set(buildingNameplate.gameObject.transform.position.x + buildingNameplate.offset.x, buildingNameplate.gameObject.transform.position.y, buildingNameplate.gameObject.transform.position.z + buildingNameplate.offset.y);
                buildingNameplate.gameObject.transform.Translate(buildingNameplate.offset.x, 0.0f, buildingNameplate.offset.y, Space.World);

#if DEBUG_Nameplates
                Vector3 posAnchor = buildingNameplate.gameObject.transform.position;
                Vector3 posNameplate = buildingNameplate.gameObject.transform.position - new Vector3(buildingNameplate.offset.x, 0.0f, buildingNameplate.offset.y);
                if (buildingNameplate.anchorLine != null)
                {
                    buildingNameplate.anchorLine.SetActive(false); // hide old line gameobject immediately (since destroy seems to be delayed this is necessary)                    
                    GameObject.Destroy(buildingNameplate.anchorLine);
                    buildingNameplate.anchorLine = null;
                }
                buildingNameplate.anchorLine = DrawLine(posAnchor, posNameplate, Color.yellow, 0.5f, 0.5f);
                buildingNameplate.anchorLine.hideFlags = HideFlags.HideAndDontSave;

                buildingNameplates[i] = buildingNameplate;

                Vector3 start1 = buildingNameplate.gameObject.transform.position + new Vector3(buildingNameplate.upperLeftCorner.x, 0.5f, buildingNameplate.upperLeftCorner.y);
                Vector3 end1 = buildingNameplate.gameObject.transform.position + new Vector3(buildingNameplate.lowerRightCorner.x, 0.5f, buildingNameplate.lowerRightCorner.y);
                Vector3 start2 = buildingNameplate.gameObject.transform.position + new Vector3(buildingNameplate.lowerLeftCorner.x, 0.5f, buildingNameplate.lowerLeftCorner.y);
                Vector3 end2 = buildingNameplate.gameObject.transform.position + new Vector3(buildingNameplate.upperRightCorner.x, 0.5f, buildingNameplate.upperRightCorner.y);

                if (buildingNameplate.debugLine1 != null)
                {
                    buildingNameplate.debugLine1.SetActive(false); // hide old line gameobject immediately (since destroy seems to be delayed this is necessary)                    
                    GameObject.Destroy(buildingNameplate.debugLine1);
                    buildingNameplate.debugLine1 = null;
                }
                if (buildingNameplate.debugLine2 != null)
                {
                    buildingNameplate.debugLine2.SetActive(false); // hide old line gameobject immediately (since destroy seems to be delayed this is necessary)                    
                    GameObject.Destroy(buildingNameplate.debugLine2);
                    buildingNameplate.debugLine2 = null;
                }

                buildingNameplate.debugLine1 = DrawLine(start1, end1, Color.blue);
                buildingNameplate.debugLine2 = DrawLine(start2, end2, Color.red);
                buildingNameplate.debugLine1.hideFlags = HideFlags.HideAndDontSave;
                buildingNameplate.debugLine2.hideFlags = HideFlags.HideAndDontSave;

                buildingNameplates[i] = buildingNameplate;
#endif
            }
        }

        private void undoNameplateOffsets()
        {
            for (int i = 0; i < buildingNameplates.Length; i++)
            {
                BuildingNameplate buildingNameplate = buildingNameplates[i];
                //buildingNameplate.gameObject.transform.position.Set(buildingNameplate.gameObject.transform.position.x - buildingNameplate.offset.x, buildingNameplate.gameObject.transform.position.y, buildingNameplate.gameObject.transform.position.z - buildingNameplate.offset.y);
                buildingNameplate.gameObject.transform.Translate(-buildingNameplate.offset.x, 0.0f, -buildingNameplate.offset.y, Space.World);
                buildingNameplate.offset = Vector2.zero;
                
                buildingNameplate.placed = false;
                buildingNameplate.gameObject.SetActive(true);

                if (buildingNameplate.nameplateReplaced)
                {
                    TextLabel newTextLabel = DaggerfallUI.AddTextLabel(customFont, Vector2.zero, buildingNameplate.name);
                    buildingNameplate.textLabel = newTextLabel;                    
                    MeshFilter meshFilter = buildingNameplate.gameObject.GetComponent<MeshFilter>();
                    meshFilter.mesh = CreateLeftAlignedMesh(buildingNameplate.textLabel.Texture.width, buildingNameplate.textLabel.Texture.height); // create left aligned (in relation to gameobject position) quad with normal facing into positive y-direction
                    MeshCollider meshCollider = buildingNameplate.gameObject.GetComponent<MeshCollider>();
                    meshCollider.sharedMesh = meshFilter.mesh;
                    MeshRenderer renderer = buildingNameplate.gameObject.GetComponent<MeshRenderer>();                    
                    renderer.material.mainTexture = newTextLabel.Texture;
                    buildingNameplate.gameObject.name = buildingNameplate.gameObject.name.Substring(0, buildingNameplate.gameObject.name.Length - 1) + "+";
                    buildingNameplate.gameObject.transform.position = new Vector3(buildingNameplate.gameObject.transform.position.x, nameplatesPlacementDepth, buildingNameplate.gameObject.transform.position.z); // set back to same height as other nameplates
                    buildingNameplate.nameplateReplaced = false;
                }

                // store changes to nameplates array
                buildingNameplates[i] = buildingNameplate;
            }
        }

        private void updatePlayerMarker()
        {
            // place player marker            
            Vector3 playerPos;
            float scale = MapsFile.WorldMapTerrainDim * MeshReader.GlobalScale;
            playerPos.x = ((GameManager.Instance.PlayerGPS.transform.position.x) % scale) / scale;
            playerPos.z = ((GameManager.Instance.PlayerGPS.transform.position.z) % scale) / scale;
            playerPos.y = 0.0f;

            int refWidth = (int)(blockSizeWidth * numMaxBlocksX * layoutMultiplier); // layoutWidth / layoutMultiplier
            int refHeight = (int)(blockSizeHeight * numMaxBlocksY * layoutMultiplier); // layoutHeight / layoutMultiplier
            playerPos.x *= refWidth;
            playerPos.y = 0.1f;
            playerPos.z *= refHeight;
            playerPos.x -= refWidth * 0.5f;
            playerPos.z -= refHeight * 0.5f;
            gameobjectPlayerMarkerArrow.transform.position = playerPos;
            gameobjectPlayerMarkerArrow.transform.rotation = gameObjectPlayerAdvanced.transform.rotation;

            // place player marker stamp
            Vector3 newPos = gameobjectPlayerMarkerArrow.transform.position;
            newPos.y = -10.0f;
            Vector3 biasVec = -Vector3.Normalize(gameObjectPlayerAdvanced.transform.forward);
            gameobjectPlayerMarkerArrowStamp.transform.position = newPos + biasVec * 0.8f;
            gameobjectPlayerMarkerArrowStamp.transform.rotation = gameobjectPlayerMarkerArrow.transform.rotation;

            newPos.y = -20.0f;
            gameobjectPlayerMarkerCircle.transform.position = newPos;
            gameobjectPlayerMarkerCircle.transform.rotation = gameobjectPlayerMarkerArrow.transform.rotation;
        }

        /// <summary>
        /// creates the map layout in the exterior layout texture
        /// </summary>  
        private void createExteriorLayoutTexture(DFLocation location, bool showAll = false, bool removeGroundFlats = true, bool createNameplates = true)
        {
            if (exteriorLayoutTexture != null)
            {
                UnityEngine.Object.Destroy(exteriorLayoutTexture);
                exteriorLayoutTexture = null;
            }

            int xpos = 0;
            int ypos = 0; //locationHeight * blockSizeHeight - blockSizeHeight;
            exteriorLayout = new BlockLayout[locationWidth * locationHeight];

            for (int y = 0; y < locationHeight; y++)
            {
                for (int x = 0; x < locationWidth; x++)
                {
                    // Get the block name
                    string blockName = DaggerfallUnity.Instance.ContentReader.BlockFileReader.CheckName(DaggerfallUnity.Instance.ContentReader.MapFileReader.GetRmbBlockName(ref location, x, y));

                    // Get the block data
                    //                    DFBlock block = DaggerfallUnity.Instance.ContentReader.BlockFileReader.GetBlock(blockName);

                    // Now we can get the automap image data for this block and lay it out
                    //block.RmbBlock.SubRecords.

                    int index = y * locationWidth + x;
                    exteriorLayout[index].x = x;
                    exteriorLayout[index].y = y;
                    exteriorLayout[index].rect = new Rectangle();
                    exteriorLayout[index].rect.xpos = xpos;
                    exteriorLayout[index].rect.ypos = ypos;
                    exteriorLayout[index].rect.width = blockSizeWidth;
                    exteriorLayout[index].rect.height = blockSizeHeight;
                    exteriorLayout[index].name = blockName;
                    exteriorLayout[index].blocktype = DFBlock.BlockTypes.Rmb;
                    exteriorLayout[index].rdbType = DFBlock.RdbTypes.Unknown;
                    xpos += blockSizeWidth;
                }
                ypos += blockSizeHeight;
                xpos = 0;
            }

            // Create layout image (texture)
            exteriorLayoutTexture = new Texture2D(layoutWidth, layoutHeight, TextureFormat.ARGB32, false);
            exteriorLayoutTexture.filterMode = FilterMode.Point;

            // Render map layout
            foreach (var layout in exteriorLayout)
            {
                DFBlock block = DaggerfallUnity.Instance.ContentReader.BlockFileReader.GetBlock(layout.name);
                DaggerfallUnity.Instance.ContentReader.BlockFileReader.LoadBlock(block.Index);
                // Get block automap image
                DFBitmap dfBitmap = DaggerfallUnity.Instance.ContentReader.BlockFileReader.GetBlockAutoMap(layout.name, removeGroundFlats);

                int size = blockSizeWidth * blockSizeHeight;
                Color32[] colors = new Color32[size];
                // for (int i = 0; i < size; i++)
                // {
                for (int y = 0; y < blockSizeHeight; y++)
                {
                    for (int x = 0; x < blockSizeWidth; x++)
                    {
                        int i = y * blockSizeWidth + x;
                        int o = (blockSizeHeight - 1 - y) * blockSizeWidth + x;
                        switch (dfBitmap.Data[i])
                        {
                            // guilds
                            case 12: // guildhall
                            case 15: // temple
                                colors[o].r = 69;
                                colors[o].g = 125;
                                colors[o].b = 195;
                                colors[o].a = 255;
                                break;
                            // shops
                            case 1: // alchemist
                            case 3: // armorer
                            case 4: // bank
                            case 6: // bookseller
                            case 7: // clothing store
                            case 9: // gem store
                            case 10: // general store
                            case 11: // library
                            case 13: // pawn shop
                            case 14: // weapon smith
                                colors[o].r = 190;
                                colors[o].g = 85;
                                colors[o].b = 24;
                                colors[o].a = 255;
                                break;
                            case 16: // tavern
                                colors[o].r = 85;
                                colors[o].g = 117;
                                colors[o].b = 48;
                                colors[o].a = 255;
                                break;
                            // common
                            case 2: // house for sale
                            case 5: // town4
                            case 8: // furniture store
                            case 17: // palace
                            case 18: // house 1
                            case 19: // house 2
                            case 20: // house 3
                            case 21: // house 4
                            case 22: // house 5 (hedge)
                            case 23: // house 6
                            case 24: // town23
                                colors[o].r = 69;
                                colors[o].g = 60;
                                colors[o].b = 40;
                                colors[o].a = 255;
                                break;
                            case 25: // ship
                            case 117: // special 1
                            case 224: // special 2
                            case 250: // special 3
                            case 251: // special 4
                                if (showAll)
                                {
                                    colors[o].r = 69;
                                    colors[o].g = 60;
                                    colors[o].b = 40;
                                    colors[o].a = 255;
                                }
                                break;
                            case 0:
                                colors[o].r = 0;
                                colors[o].g = 0;
                                colors[o].b = 0;
                                colors[o].a = 0;
                                break;
                            default: // unknown
                                colors[o].r = 255;
                                colors[o].g = 0;
                                colors[o].b = dfBitmap.Data[i];
                                colors[o].a = 255;
                                break;
                        }
                    }
                }

                exteriorLayoutTexture.SetPixels32(layout.rect.xpos, layout.rect.ypos, layout.rect.width, layout.rect.height, colors);

                //RMBLayout.BuildingSummary[] buildingsInBlock = RMBLayout.GetBuildingData(block);
                //foreach (RMBLayout.BuildingSummary buildingSummary in buildingsInBlock)
                //{
                //    //Debug.Log(String.Format("x: {0}, y: {1}", buildingSummary.Position.x, buildingSummary.Position.z));
                //    int xPosBuilding = layout.rect.xpos + (int)(buildingSummary.Position.x / (BlocksFile.RMBDimension * MeshReader.GlobalScale) * blockSizeWidth);
                //    int yPosBuilding  = layout.rect.ypos + (int)(buildingSummary.Position.z / (BlocksFile.RMBDimension * MeshReader.GlobalScale) * blockSizeHeight);
                //    //exteriorLayoutTexture.SetPixel(xPosBuilding, yPosBuilding, Color.yellow);
                //}

                exteriorLayoutTexture.Apply();

                DaggerfallUnity.Instance.ContentReader.BlockFileReader.DiscardBlock(block.Index);
            }

            if (createNameplates)
            {
                createBuildingNameplates(location);
            }
        }

        private void loadAndCreateLocationExteriorAutomap()
        {
            ContentReader.MapSummary mapSummary;
            DFPosition mapPixel = GameManager.Instance.PlayerGPS.CurrentMapPixel;
            if (!DaggerfallUnity.Instance.ContentReader.HasLocation(mapPixel.X, mapPixel.Y, out mapSummary))
            {
                // no location found
                return; // do nothing
            }

            DFLocation currentPlayerLocation = DaggerfallUnity.Instance.ContentReader.MapFileReader.GetLocation(mapSummary.RegionIndex, mapSummary.MapIndex);
            if (!currentPlayerLocation.Loaded)
            {
                // Location not loaded, something went wrong
                DaggerfallUnity.LogMessage("error when loading location for exterior automap layouting", true);
                if (Application.isEditor)
                    Debug.Break();
                else
                    Application.Quit();
            }

            if ((location.Loaded) && (currentPlayerLocation.Name == location.Name)) // if already loaded
            {
                return; // do nothing
            }

            unloadLocationExteriorAutomap(); // first make sure to unload location exterior automap and destroy resources

            location = currentPlayerLocation; // set current location as new location

            // and now layout it

            // Get location dimensions
            locationWidth = location.Exterior.ExteriorData.Width;
            locationHeight = location.Exterior.ExteriorData.Height;

            // Get layout image dimensions
            layoutWidth = locationWidth * blockSizeWidth;
            layoutHeight = locationHeight * blockSizeHeight;

            // Create map layout
            switch (currentExteriorAutomapViewMode)
            {
                case ExteriorAutomapViewMode.Original:
                    createExteriorLayoutTexture(location, false, true);
                    break;
                case ExteriorAutomapViewMode.Extra:
                    createExteriorLayoutTexture(location, true, true);
                    break;
                case ExteriorAutomapViewMode.All:
                    createExteriorLayoutTexture(location, true, false);
                    break;
            }

            // create player marker
            if (!gameobjectPlayerMarkerArrow)
            {
                gameobjectPlayerMarkerArrow = GameObjectHelper.CreateDaggerfallMeshGameObject(99900, gameobjectExteriorAutomap.transform, false, null, true);
                gameobjectPlayerMarkerArrow.name = "PlayerMarkerArrow";
                gameobjectPlayerMarkerArrow.layer = layerAutomap;
                Material oldMat = gameobjectPlayerMarkerArrow.GetComponent<MeshRenderer>().material;
                Material newMat = new Material(oldMat);
                newMat.shader = Shader.Find("Unlit/Texture");
                //newMat.CopyPropertiesFromMaterial(oldMat);
                gameobjectPlayerMarkerArrow.GetComponent<MeshRenderer>().material = newMat;
                gameobjectPlayerMarkerArrow.transform.localScale = new Vector3(2.5f, 2.5f, 2.5f) * layoutMultiplier;
            }

            // create player marker stamp (a darkened larger version in the background)
            if (!gameobjectPlayerMarkerArrowStamp)
            {
                gameobjectPlayerMarkerArrowStamp = GameObjectHelper.CreateDaggerfallMeshGameObject(99900, gameobjectExteriorAutomap.transform, false, null, true);
                gameobjectPlayerMarkerArrowStamp.name = "PlayerMarkerArrowStamp";
                gameobjectPlayerMarkerArrowStamp.layer = layerAutomap;
                Material oldMat = gameobjectPlayerMarkerArrowStamp.GetComponent<MeshRenderer>().material;
                Material newMat = new Material(oldMat);
                newMat.shader = Shader.Find("Unlit/Color");
                newMat.color = new Color(0.353f, 0.086f, 0.086f);
                //newMat.CopyPropertiesFromMaterial(oldMat);
                gameobjectPlayerMarkerArrowStamp.GetComponent<MeshRenderer>().material = newMat;
                gameobjectPlayerMarkerArrowStamp.transform.localScale = new Vector3(4.0f, 4.0f, 4.0f) * layoutMultiplier;
            }

            // create player circle
            if (!gameobjectPlayerMarkerCircle)
            {
                gameobjectPlayerMarkerCircle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                CapsuleCollider capsuleCollider = gameobjectPlayerMarkerCircle.GetComponent<CapsuleCollider>();
                if (capsuleCollider)
                {
                    // get rid of capsule collider
                    UnityEngine.Object.Destroy(capsuleCollider);
                }
                gameobjectPlayerMarkerCircle.name = "PlayerMarkerCircle";
                gameobjectPlayerMarkerCircle.layer = layerAutomap;
                gameobjectPlayerMarkerCircle.transform.SetParent(gameobjectExteriorAutomap.transform);
                Material oldMat = gameobjectPlayerMarkerCircle.GetComponent<MeshRenderer>().material;
                Material newMat = new Material(oldMat);
                newMat.shader = Shader.Find("Unlit/Color");
                newMat.color = new Color(0.75f, 0.71f, 0.71f);
                //newMat.CopyPropertiesFromMaterial(oldMat);
                gameobjectPlayerMarkerCircle.GetComponent<MeshRenderer>().material = newMat;
                gameobjectPlayerMarkerCircle.transform.localScale = new Vector3(12.0f, 1.0f, 12.0f) * layoutMultiplier;
            }

            updatePlayerMarker();

            //byte[] png = exteriorLayoutTexture.EncodeToPNG();
            //Debug.Log(String.Format("writing to folder {0}", Application.dataPath));
            //File.WriteAllBytes(Application.dataPath + "/test.png", png);

            createCustomCanvasForExteriorAutomap();

            ResetAutomapSettingsSignalForExternalScript = true; // force automap settings reset in next OnPush() function of DaggerfallExteriorAutomapWindow (for reset of camera settings)

            location.Loaded = true;
        }

        private void unloadLocationExteriorAutomap()
        {
            deleteBuildingNameplates();

            if (gameobjectCustomCanvas != null)
            {
                UnityEngine.Object.Destroy(gameobjectCustomCanvas);
                gameobjectCustomCanvas = null;
            }

            if (gameobjectPlayerMarkerArrow)
            {
                UnityEngine.Object.Destroy(gameobjectPlayerMarkerArrow);
                gameobjectPlayerMarkerArrow = null;
            }

            if (gameobjectPlayerMarkerArrowStamp)
            {
                UnityEngine.Object.Destroy(gameobjectPlayerMarkerArrowStamp);
                gameobjectPlayerMarkerArrowStamp = null;
            }

            if (gameobjectPlayerMarkerCircle)
            {
                UnityEngine.Object.Destroy(gameobjectPlayerMarkerCircle);
                gameobjectPlayerMarkerCircle = null;
            }

            if (exteriorLayoutTexture != null)
            {
                UnityEngine.Object.Destroy(exteriorLayoutTexture);
                exteriorLayoutTexture = null;
            }

            location.Loaded = false;
        }

        private void OnMapPixelChanged(DFPosition mapPixel)
        {
            ContentReader.MapSummary mapSummary;
            if (DaggerfallUnity.Instance.ContentReader.HasLocation(mapPixel.X, mapPixel.Y, out mapSummary))
            {
                loadAndCreateLocationExteriorAutomap();
            }
        }

        private void OnTransitionToInterior(PlayerEnterExit.TransitionEventArgs args)
        {
            gameobjectExteriorAutomap.SetActive(false);
        }

        private void OnTransitionToDungeonInterior(PlayerEnterExit.TransitionEventArgs args)
        {
            gameobjectExteriorAutomap.SetActive(false);
        }

        private void OnTransitionToExterior(PlayerEnterExit.TransitionEventArgs args)
        {
            gameobjectExteriorAutomap.SetActive(true);
            DFPosition mapPixel = GameManager.Instance.PlayerGPS.CurrentMapPixel;
            ContentReader.MapSummary mapSummary;
            if (DaggerfallUnity.Instance.ContentReader.HasLocation(mapPixel.X, mapPixel.Y, out mapSummary))
            {
                loadAndCreateLocationExteriorAutomap();
            }
        }

        private void OnTransitionToDungeonExterior(PlayerEnterExit.TransitionEventArgs args)
        {
            gameobjectExteriorAutomap.SetActive(true);
            DFPosition mapPixel = GameManager.Instance.PlayerGPS.CurrentMapPixel;
            ContentReader.MapSummary mapSummary;
            if (DaggerfallUnity.Instance.ContentReader.HasLocation(mapPixel.X, mapPixel.Y, out mapSummary))
            {
                loadAndCreateLocationExteriorAutomap();
            }
        }

        void OnLoadEvent(SaveData_v1 saveData)
        {
            if (!GameManager.Instance.IsPlayerInside)
            {
                gameobjectExteriorAutomap.SetActive(true);
                DFPosition mapPixel = GameManager.Instance.PlayerGPS.CurrentMapPixel;
                ContentReader.MapSummary mapSummary;
                if (DaggerfallUnity.Instance.ContentReader.HasLocation(mapPixel.X, mapPixel.Y, out mapSummary))
                {
                    loadAndCreateLocationExteriorAutomap();
                }
            }
            else
            {
                gameobjectExteriorAutomap.SetActive(false);
            }
        }
        #endregion

        #region console_commands

        public static class ExteriorAutoMapConsoleCommands
        {
            public static void RegisterCommands()
            {
                try
                {
                    ConsoleCommandsDatabase.RegisterCommand(RevealBuildings.name, RevealBuildings.description, RevealBuildings.usage, RevealBuildings.Execute);
                    ConsoleCommandsDatabase.RegisterCommand(HideBuildings.name, HideBuildings.description, HideBuildings.usage, HideBuildings.Execute);
                }
                catch (System.Exception ex)
                {
                    DaggerfallUnity.LogMessage(ex.Message, true);
                }
            }

            private static class RevealBuildings
            {
                public static readonly string name = "map_revealbuildings";
                public static readonly string description = "Reveals undiscovered buildings on exterior automap (temporary)";
                public static readonly string usage = "map_revealbuildings";


                public static string Execute(params string[] args)
                {
                    if (GameManager.Instance.IsPlayerInside)
                    {
                        return "this command only has an effect when outside and at a location";
                    }

                    DaggerfallExteriorAutomap daggerfallExteriorAutomap = DaggerfallExteriorAutomap.instance;
                    if (daggerfallExteriorAutomap == null)
                    {
                        return "DaggerfallExteriorAutomap instance not found";
                    }

                    daggerfallExteriorAutomap.RevealUndiscoveredBuildings = true;
                    return "undiscovered buildings have been revealed (temporary) on the exterior automap";
                }
            }

            private static class HideBuildings
            {
                public static readonly string name = "map_hidebuildings";
                public static readonly string description = "Hides undiscovered buildings on exterior automap";
                public static readonly string usage = "map_hidebuildings";


                public static string Execute(params string[] args)
                {
                    if (GameManager.Instance.IsPlayerInside)
                    {
                        return "this command only has an effect when outside and at a location";
                    }

                    DaggerfallExteriorAutomap daggerfallExteriorAutomap = DaggerfallExteriorAutomap.instance;
                    if (daggerfallExteriorAutomap == null)
                    {
                        return "DaggerfallExteriorAutomap instance not found";
                    }

                    daggerfallExteriorAutomap.RevealUndiscoveredBuildings = false;

                    return "undiscovered buildings have been hidden on the exterior automap again";
                }

            }
        }

        #endregion
    }
}