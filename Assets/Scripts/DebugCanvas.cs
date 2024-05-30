using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ImageRecognition;
using UnityEngine.XR.ARFoundation;


public class DebugCanvas : MonoBehaviour
{
    public TMPro.TextMeshProUGUI text;
    public RawImage image;
    public GameObject button;
    public GameObject downLeft;
    public GameObject downRight;
    public GameObject upLeft;
    public GameObject upRight;
    public ImageRecognitionManager imageRecognitionManager;
    public GameObject[] debugObjectPrefabs;

    private MatchedTarget? lastTarget = null;
    private int timeSinceScan = 0;
    private Dictionary<string, GameObject> instantiatedObjects = new Dictionary<string, GameObject>();
    private Dictionary<string, int> instantiatedObjectTypes = new Dictionary<string, int>();

    private void Awake()
    {
        imageRecognitionManager.imageRecognitionEvent.AddListener(OnImageRecognition);

        Button b = button.GetComponent<Button>();
        b.onClick.AddListener(CreateObject);
    }


    void CreateObject()
    {
        if (lastTarget is MatchedTarget matchedTarget && timeSinceScan < 20)
        {
            string name = matchedTarget.imageTarget.xrReferenceImage.name;
            int debugObjectNum;
            if (instantiatedObjects.ContainsKey(name))
            {
                Destroy(instantiatedObjects[name]);
                debugObjectNum = instantiatedObjectTypes[name];
            }
            else
            {
                debugObjectNum = instantiatedObjects.Count % debugObjectPrefabs.Length;
                instantiatedObjectTypes[name] = debugObjectNum;
            }

            GameObject instance = Instantiate(debugObjectPrefabs[debugObjectNum], matchedTarget.position, matchedTarget.orientation);

            instance.AddComponent<ARAnchor>();
            instantiatedObjects[name] = instance;

        }

    }

    void OnImageRecognition(MatchedTarget matchedTarget)
    {
        lastTarget = matchedTarget;
        timeSinceScan = 0;
    }

    void Update()
    {
        timeSinceScan += 1;
        if (lastTarget is MatchedTarget matchedTarget && timeSinceScan < 20)
        {
            image.enabled = true;
            image.texture = matchedTarget.imageTarget.xrReferenceImage.texture;
            text.enabled = true;
            text.text = matchedTarget.ToString();
            button.SetActive(true);
            upLeft.SetActive(true);
            upRight.SetActive(true);
            downLeft.SetActive(true);
            downRight.SetActive(true);

            float pixelWidth = matchedTarget.scale * matchedTarget.imageTarget.xrReferenceImage.texture.width * Mathf.Pow(2, matchedTarget.imageTarget.scalePower);
            float pixelHeight = matchedTarget.scale * matchedTarget.imageTarget.xrReferenceImage.texture.height * Mathf.Pow(2, matchedTarget.imageTarget.scalePower);
            downLeft.GetComponent<RectTransform>().anchoredPosition = new Vector3(matchedTarget.origin.x, matchedTarget.origin.y, 0);
            downRight.GetComponent<RectTransform>().anchoredPosition = new Vector3(matchedTarget.origin.x + pixelWidth, matchedTarget.origin.y, 0);
            upLeft.GetComponent<RectTransform>().anchoredPosition = new Vector3(matchedTarget.origin.x, matchedTarget.origin.y + pixelHeight, 0);
            upRight.GetComponent<RectTransform>().anchoredPosition = new Vector3(matchedTarget.origin.x + pixelWidth, matchedTarget.origin.y + pixelHeight, 0);
        }
        else
        {
            image.enabled = false;
            text.enabled = true;
            text.text = "no matches\n";
            button.SetActive(false);
            upLeft.SetActive(false);
            upRight.SetActive(false);
            downLeft.SetActive(false);
            downRight.SetActive(false);
        }
    }
}
