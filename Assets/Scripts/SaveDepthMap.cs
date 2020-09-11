using System.Collections;
using System.Collections.Generic;
using System;
using System.IO;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.UI;
using System.Text;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

public class SaveDepthMap : MonoBehaviour
{
    public ARCameraManager cm;
    public RawImage[] depths;
    public RawImage imgShow;

    private RenderTexture[] rts = new RenderTexture[3];

    private Texture2D texBuffer;
    private Texture2D[] texShow = new Texture2D[3];
    byte[] bytes;
    bool textureUpdate = false;

    Texture2D t4;
    NativeArray<byte> b4;

    // save 1F flag
    bool save = false;

    // rec flag
    bool rec = false;
    int saveRestCount = 0;
    int saveCount = 0;
    DateTime startRec;

    public Button ButtonStartRec;
    public InputField InputFrameCount;

    public Text TextFPS;
    int frameCount = 0;
    float lastUpdate;
    float nextUpdate;

    private String[] names =
    {
        ".env", ".hud", ".hus",
    };

    // Start is called before the first frame update
    void Start()
    {
        lastUpdate = Time.realtimeSinceStartup;
        nextUpdate = lastUpdate + 1.0f;
    }

    // Update is called once per frame
    void Update()
    {
        // update fps
        {
            frameCount++;

            float current = Time.realtimeSinceStartup;
            if (nextUpdate <= current)
            {
                float fps = frameCount / (current - lastUpdate);
                TextFPS.text = "FPS: " + String.Format("{0:00.0}", fps);
                lastUpdate = current;
                nextUpdate = current + 1.0f;
                frameCount = 0;
            }
        }

        bool saveCurrent = (save || (rec && saveRestCount > 0));

        // 0: Env, 1: HumanDepth, 2: HumanStencil
        for (int d = 0; d < 3; d++)
        {
            Texture2D depthMap = (Texture2D)depths[d].texture;

            if (depthMap == null || depthMap.width <= 0)
            {
                Debug.Log("depth[" + d + "] is invalid. " + depthMap);
                continue;
            }

            if (rts[d] == null)
            {
                rts[d] = RenderTexture.GetTemporary(
                    depthMap.width, depthMap.height, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Default);
            }
            Graphics.Blit(depthMap, rts[d]);
            RenderTexture pre = RenderTexture.active;
            RenderTexture.active = rts[d];

            // envDepth(png), csv, color

            if (d == 0 && texShow[0] == null)
            {
                texShow[0] = new Texture2D(rts[d].width, rts[d].height, TextureFormat.RGBA32, false);
                imgShow.texture = texShow[0];
                bytes = new byte[rts[d].width * rts[d].height * 4];
                // fill alpha
                for (int i = 0; i < rts[d].width * rts[d].height; i++)
                {
                    bytes[i * 4 + 3] = (byte)255;
                }
            }

            if (texBuffer == null)
            {
                texBuffer = new Texture2D(rts[d].width, rts[d].height, TextureFormat.RFloat, false);
            }
            texBuffer.ReadPixels(new Rect(0, 0, rts[d].width, rts[d].height), 0, 0);
            texBuffer.Apply();

            RenderTexture.active = pre;

            Color[] cs = texBuffer.GetPixels();

            if (d == 0)
            {
                // make color scale

                const float range7 = 8.0f / 7; // 8m まで
                const float range2557 = 255.0f / range7;
                const float range1277 = 127.5f / range7;
                for (int y = rts[d].height; y > 0;)
                {
                    y--;

                    for (int x = 0; x < rts[d].width; x++)
                    {
                        int j = (rts[d].height - 1 - y) * rts[d].width + x;
                        float f = cs[j].r;

                        int i = y * rts[d].width + x;

                        byte r = 0;
                        byte g = 0;
                        byte b = 0;
                        if (f < range7)
                        {
                            b = (byte)(f * range2557);
                        }
                        else if (f < range7 * 2)
                        {
                            b = (byte)255;
                            g = (byte)((f - range7) * range1277);
                        }
                        else if (f < range7 * 3)
                        {
                            g = (byte)((f - range7 * 2) * range1277 + 127.5f);
                            b = (byte)((range7 * 3 - f) * range2557);
                        }
                        else if (f < range7 * 4)
                        {
                            g = (byte)255;
                            r = (byte)((f - range7 * 3) * range2557);
                        }
                        else if (f < range7 * 5)
                        {
                            r = (byte)255;
                            g = (byte)((range7 * 5 - f) * range2557);
                        }
                        else if (f < range7 * 6)
                        {
                            r = (byte)255;
                            b = (byte)((f - range7 * 5) * range2557);
                        }
                        else if (f < range7 * 7)
                        {
                            r = (byte)255;
                            b = (byte)255;
                            g = (byte)((f - range7 * 6) * range2557);
                        }
                        else
                        {
                            r = (byte)255;
                            b = (byte)255;
                            g = (byte)255;
                        }

                        bytes[i * 4 + 0] = r;
                        bytes[i * 4 + 1] = g;
                        bytes[i * 4 + 2] = b;
                    }
                }

                texShow[0].LoadRawTextureData(bytes);
                texShow[0].Apply();
            }

            if (saveCurrent)
            {
                Debug.Log("save depth.");

                save = false;

                StringBuilder sb = new StringBuilder();
                for (int y = rts[d].height; y > 0;)
                {
                    y--;

                    for (int x = 0; x < rts[d].width; x++)
                    {
                        int j = (rts[d].height - 1 - y) * rts[d].width + x;
                        float f = cs[j].r;

                        if (x != 0) sb.Append(",");
                        sb.Append(f);
                    }

                    sb.AppendLine();
                }

                string fileName;
                if (rec)
                {
                    // rec frames

                    saveCount++;
                    DateTime now = startRec;
                    fileName = keta(now.Year, 2) + keta(now.Month, 2) + keta(now.Day, 2) + keta(now.Hour, 2) + keta(now.Minute, 2) + keta(now.Second, 2);
                    fileName = fileName + "_" + keta(saveCount, 2);
                    saveRestCount--;
                    if (saveRestCount <= 0)
                    {
                        rec = false;
                        ButtonStartRec.GetComponentInChildren<Text>().text = "Start Rec.";
                    }
                    else
                    {
                        ButtonStartRec.GetComponentInChildren<Text>().text = "Stop (" + saveCount + ")";
                    }
                }
                else
                {
                    // save 1 frame
                    DateTime now = DateTime.Now;
                    fileName = keta(now.Year, 2) + keta(now.Month, 2) + keta(now.Day, 2) + keta(now.Hour, 2) + keta(now.Minute, 2) + keta(now.Second, 2);
                }

                if (d == 0)
                {
                    // color(jpg) & depth(png)
                    XRCpuImage image;
                    if (cm.TryAcquireLatestCpuImage(out image))
                    {
                        var conversionParams = new XRCpuImage.ConversionParams
                        {
                            inputRect = new RectInt(0, 0, image.width, image.height),
                            outputDimensions = new Vector2Int(image.width / 2, image.height / 2),
                            outputFormat = TextureFormat.RGBA32,
                            transformation = XRCpuImage.Transformation.MirrorX
                        };
                        int size = image.GetConvertedDataSize(conversionParams);
                        if (b4 == null || b4.Length != size)
                        {
                            if (b4 != null)
                            {
                                b4.Dispose();
                            }
                            b4 = new NativeArray<byte>(size, Allocator.Temp);
                        }
                        unsafe
                        {
                            image.Convert(conversionParams, new IntPtr(b4.GetUnsafePtr()), b4.Length);
                        }
                        image.Dispose();

                        if (t4 == null
                            || t4.width != conversionParams.outputDimensions.x
                            || t4.height != conversionParams.outputDimensions.y
                            || t4.format != conversionParams.outputFormat)
                        {
                            t4 = new Texture2D(
                                conversionParams.outputDimensions.x,
                                conversionParams.outputDimensions.y,
                                conversionParams.outputFormat, false);
                        }
                        t4.LoadRawTextureData(b4);
                        t4.Apply();

                        byte[] jpg = t4.EncodeToJPG();
                        File.WriteAllBytes(Application.persistentDataPath + "/" + fileName + ".jpg", jpg);
                    }

                    byte[] png = texShow[0].EncodeToPNG();
                    File.WriteAllBytes(Application.persistentDataPath + "/" + fileName + names[d] + ".png", png);
                }

                // csv
                File.WriteAllText(Application.persistentDataPath + "/" + fileName + names[d] + ".csv", sb.ToString());
            }
        }
    }

    public void OnPressed()
    {
        save = true;
    }

    public void OnPressedRec()
    {
        if (rec)
        {
            rec = false;
            saveRestCount = 0;
            saveCount = 0;
            ButtonStartRec.GetComponentInChildren<Text>().text = "Start Rec.";
        }
        else
        {
            rec = true;
            startRec = DateTime.Now;
            saveRestCount = int.Parse(InputFrameCount.text);
            saveCount = 0;
            ButtonStartRec.GetComponentInChildren<Text>().text = "Stop (" + saveCount + ")";
        }
    }

    private string keta(int value, int keta)
    {
        string s = "" + value;
        while (s.Length < keta) s = "0" + s;

        return s;
    }
}
