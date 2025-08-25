using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

public class ShadowMapGenerator : MonoBehaviour
{
    //public Texture2D shadowMap;
    public GameObject geometry;
    public Camera lightCamera;
    public Light mainLight;
    public bool ifCreatingShadowMap = true;
    private bool capture;
    bool hasSet;
    private List<float> timesForFrames;
    private bool countsFPS;
    // Start is called before the first frame update
    void Start()
    {
        hasSet = false;
        capture = false;
        countsFPS = false;
        StartShadowing();
        Camera.onPostRender += OnPostRenderCallback;
    }

    private void StartShadowing()
    {
        if (ifCreatingShadowMap)
        {
            UpdateShadowMapShader(geometry, "Custom/Shadow_map");
            Matrix4x4 mainLightViewProjectionMatrix = lightCamera.projectionMatrix * lightCamera.worldToCameraMatrix;
            Debug.Log($"{mainLightViewProjectionMatrix}");
            Shader.SetGlobalMatrix("_LightViewProjection", mainLightViewProjectionMatrix);
        } else
        {
            UpdateChildPureColorShadow(geometry);
            //byte[] shadowMapbytes = File.ReadAllBytes(Application.dataPath + "./Resources/Materials/Textures/shadowMap_new.png");
            //Texture2D shadowMap = new Texture2D(2, 2);
            //shadowMap.LoadImage(shadowMapbytes);
            Texture2D shadowMap = Resources.Load<Texture2D>("Materials/Textures/shadowMap_new");
            //Matrix4x4 mainLightViewProjectionMatrix = lightCamera.projectionMatrix * lightCamera.worldToCameraMatrix;

            //Matrix4x4 mainLightViewProjectionMatrix = new Matrix4x4(new Vector4(0.00694f, 0, 0, 0),
            //    new Vector4(0, 0.00557f, -0.00119f, 0),
            //    new Vector4(0, 0.00415f, 0.00160f, 0),
            //    new Vector4(0.47222f, -0.18784f, -0.50314f, 1));
            Matrix4x4 mainLightViewProjectionMatrix = new Matrix4x4(new Vector4(0.00631f, 0, 0, 0),
                new Vector4(0, 0.02025f, -0.00119f, 0),
                new Vector4(0, 0.01508f, 0.00160f, 0),
                new Vector4(0.42929f, -2.66236f, -0.50335f, 1));
            Debug.Log($"{mainLightViewProjectionMatrix}");
            Shader.SetGlobalTexture("_CustomShadowMap", shadowMap);
            Shader.SetGlobalMatrix("_LightViewProjection", mainLightViewProjectionMatrix);
        }
    }

    void OnPostRenderCallback(Camera cam)
    {
        if (!capture)
            return;
        Debug.Log($"{cam.name}, {cam.tag}");
        if (cam.tag == "LightCamera")
        {
            capture = false;
            int xPosToWriteTo = 0, yPosToWriteTo = 0;
            bool updateMipMapsAutomatically = false;

            //int screenWidth = cam.activeTexture.width, screenHeight = cam.activeTexture.height;
            int screenWidth = 4096, screenHeight = 1024;
            Texture2D destinationTexture = new Texture2D(screenWidth, screenHeight, TextureFormat.RGBA32, false);

            RenderTexture.active = cam.targetTexture;
            Rect regionToReadFrom = new Rect(0, 0, screenWidth, screenHeight);
            destinationTexture.ReadPixels(regionToReadFrom, xPosToWriteTo, yPosToWriteTo, updateMipMapsAutomatically);
            destinationTexture.Apply();
            SaveTextureToFile(destinationTexture, "./Resources/Materials/Textures/shadowMap_new.png");

            RenderTexture.active = null;            
            //UpdateShadowMapShader(geometry, "Custom/Shadow_map_alt");
            //Shader.SetGlobalTexture("_CustomShadowMap", destinationTexture);
            //Matrix4x4 mainLightViewProjectionMatrix = lightCamera.projectionMatrix * lightCamera.worldToCameraMatrix;
            //Shader.SetGlobalMatrix("_LightViewProjection", mainLightViewProjectionMatrix);
        }
    }


    public void UpdateShadowMapShader(GameObject parent, string shadowMapShader)
    {
        MeshRenderer renderer = parent.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material newMaterial = new Material(Shader.Find(shadowMapShader));
                Material material = materials[i];
                if (material == null)
                    continue;
                int mode = material.GetInt("_Mode");
                if (mode == 1)
                {
                    newMaterial.SetFloat("_Cutoff", material.GetFloat("_Cutoff"));
                }
                else
                {
                    newMaterial.SetFloat("_Cutoff", 0);
                }
                newMaterial.SetTexture("_MainTex", material.GetTexture("_MainTex"));
                materials[i] = newMaterial;
            }
            renderer.materials = materials;
        }

        for (int i = 0; i < parent.transform.childCount; i++)
        {
            UpdateShadowMapShader(parent.transform.GetChild(i).gameObject, shadowMapShader);
        }
    }

    // Update is called once per frame
    void Update()
    {
        //Texture texture = Shader.GetGlobalTexture("_ShadowMapTexture");
        //if(texture.width >= 1000 && !hasSet)
        //{
        //    Texture2D texture2D = ConvertToTexture2D(texture);
        //    //SaveTextureToFile(texture2D, "Resources/Materials/Textures/shadowMap.png");
        //    //lightCamera.gameObject.SetActive(true);
        //    UpdateChild(geometry);
        //    Shader.SetGlobalVector("_EnvPos", camera.transform.position);
        //    Shader.SetGlobalVector("_UserPos", camera.transform.position);
        //    Shader.SetGlobalTexture("_CustomShadowMap", texture2D);
        //    //Shader.SetGlobalTexture("_CustomShadowMap", texture);
        //    //lightCamera.gameObject.SetActive(false);
        //    hasSet = true;
        //}
        if (Keyboard.current.sKey.wasPressedThisFrame)
        {
            capture = true;
        }
        //Matrix4x4 mainLightViewProjectionMatrix = lightCamera.projectionMatrix * lightCamera.worldToCameraMatrix;
        //Shader.SetGlobalMatrix("_LightViewProjection", mainLightViewProjectionMatrix);
    }

    

    private Texture2D ConvertToTexture2D(Texture texture)
    {
        RenderTexture prevRenderTexture = RenderTexture.active;
        RenderTexture renderTexture = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);

        Graphics.Blit(texture, renderTexture);
        RenderTexture.active = renderTexture;

        Texture2D texture2D = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
        texture2D.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        texture2D.Apply();

        RenderTexture.active = prevRenderTexture;
        RenderTexture.ReleaseTemporary(renderTexture);

        return texture2D;
    }

    private void SaveTextureToFile(Texture2D texture, string fileName)
    {
        byte[] bytes = texture.EncodeToPNG();
        string path = Path.Combine(Application.dataPath, fileName);
        File.WriteAllBytes(path, bytes);
        Debug.Log($"Shadow map saved to: {path}");
    }

    private void UpdateChild(GameObject parent)
    {
        MeshRenderer renderer = parent.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null)
                    continue;

                int mode = material.GetInt("_Mode");
                Material newMaterial = new Material(Shader.Find("Custom/Morph_alt"));
                //Material newMaterial = new Material(Shader.Find("Custom/VC_Morph"));
                if (mode == 1)
                {
                    //newMaterial.SetFloat("_IsCutout", 1);
                    newMaterial.SetFloat("_Cutoff", material.GetFloat("_Cutoff"));
                }
                else
                {
                    newMaterial.SetFloat("_Cutoff", 0);
                }
                newMaterial.SetColor("_Color", material.GetColor("_Color"));
                newMaterial.SetFloat("_IsColor", 1);
                newMaterial.SetTexture("_MainTex", material.GetTexture("_MainTex"));
                newMaterial.SetInt("_IsMorph", 0);
                newMaterial.SetFloat("_NearR", 100 * 0.3f);
                newMaterial.SetFloat("_MorphR", 100);
                newMaterial.SetFloat("_InvisibleR", 0);
                materials[i] = newMaterial;
            }
            renderer.materials = materials;

            //parent.AddComponent<RendererControl>();
        }

        for (int i = 0; i < parent.transform.childCount; i++)
        {
            UpdateChild(parent.transform.GetChild(i).gameObject);
        }
    }

    private void UpdateChildPureColorShadow(GameObject parent)
    {
        MeshRenderer renderer = parent.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null)
                    continue;

                int mode = material.GetInt("_Mode");
                Material newMaterial = new Material(Shader.Find("Custom/Pure_Color_Shadow"));
                if (mode == 1)
                {
                    //newMaterial.SetFloat("_IsCutout", 1);
                    newMaterial.SetFloat("_Cutoff", material.GetFloat("_Cutoff"));
                }
                else
                {
                    newMaterial.SetFloat("_Cutoff", 0);
                }
                newMaterial.SetColor("_Color", material.GetColor("_Color"));
                materials[i] = newMaterial;
            }
            renderer.materials = materials;
        }

        for (int i = 0; i < parent.transform.childCount; i++)
        {
            UpdateChildPureColorShadow(parent.transform.GetChild(i).gameObject);
        }
    }
}
