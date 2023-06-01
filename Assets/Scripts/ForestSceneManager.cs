using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Linq;
using TMPro;

[RequireComponent(typeof(ARTrackedImageManager))]
public class ForestSceneManager : MonoBehaviour
{
	// [SerializeField] private string[] demoImageNames;
	// [SerializeField] private Transform[] demoImageTransforms;

    [SerializeField] private ForestScene[] forestScenes;
	[Tooltip("The ForestScene will be positioned based on the most recently added tracked image or the most recently tracked image within maxRetrackDistance in the camera view")]
	[SerializeField] private float maxRetrackDistance = 2.0f;
	[Tooltip("Text box in which to show debug information.")]
	[SerializeField] private TMP_Text debugText;

	private ARTrackedImageManager arTrackedImageManager;
	private Dictionary<string, ForestScene> imageToScene = new Dictionary<string, ForestScene>();

	private Dictionary<string, ARTrackedImage> trackedImages = new Dictionary<string, ARTrackedImage>();
	private List<string> trackedImagesOrder = new List<string>();
	private List<string> limitedImagesOrder = new List<string>();
	private string currentImageName = "";
	private ForestScene currentForestScene;


	private List<string> pointedImages = new List<string>();

	private void Awake()
	{
		arTrackedImageManager = GetComponent<ARTrackedImageManager>();
	}

    private void Start()
    {
		foreach (ForestScene fs in forestScenes)
		{
			fs.gameObject.SetActive(false);
			foreach (string img in fs.imageToTransform.Keys)
			{
				imageToScene[img] = fs;
			}
		}
	}

    void OnEnable() => arTrackedImageManager.trackedImagesChanged += OnChanged;

    void OnDisable() => arTrackedImageManager.trackedImagesChanged -= OnChanged;

	void OnChanged(ARTrackedImagesChangedEventArgs eventArgs)
	{
		foreach (ARTrackedImage newImage in eventArgs.added)
		{
			UpdateImages(newImage);
		}

		foreach (ARTrackedImage updatedImage in eventArgs.updated)
		{
			UpdateImages(updatedImage);
		}

		foreach (ARTrackedImage removedImage in eventArgs.removed)
		{
			UpdateImages(removedImage);
		}
	}

	private void UpdateImages(ARTrackedImage image, bool retrack=false)
	{
		string imageName = image.referenceImage.name;
		switch (image.trackingState)
		{
			case TrackingState.Tracking:
				trackedImages[imageName] = image;
				trackedImagesOrder.Remove(imageName);
				trackedImagesOrder.Add(imageName);
				limitedImagesOrder.Remove(imageName);
				break;
			case TrackingState.Limited:
				// filter out duplicate limited tracking events
				if (!limitedImagesOrder.Contains(imageName) || retrack) {
					trackedImages[imageName] = image;
					limitedImagesOrder.Remove(imageName);
					limitedImagesOrder.Add(imageName);
					trackedImagesOrder.Remove(imageName);
				}
				break;
			default: // TrackingState.None
				trackedImages.Remove(imageName);
				trackedImagesOrder.Remove(imageName);
				limitedImagesOrder.Remove(imageName);
				break;
		}
	}

	private void UpdateViewedImages()
    {
		// test if the camera is currently pointed at a tracked image
		// if so, set that image to the top of its limited or tracked order
		Vector3 camDirection = Camera.main.transform.forward;
		float minZ = maxRetrackDistance;
		ARTrackedImage pointedImage = null;

		pointedImages.Clear();
		foreach (string n in trackedImages.Keys)
		{
			Transform t = trackedImages[n].transform;
			Vector3 imgDirection = t.up;
			Vector3 p = Camera.main.WorldToViewportPoint(t.position);
			// check if the image center is within the viewport and close enough to the camera and facing the camera
			if (p.x > 0 && p.x < 1 && p.y > 0 && p.y < 1 && p.z > 0 && p.z < maxRetrackDistance && Vector3.Angle(camDirection, imgDirection) > 120)
			{
				pointedImages.Add(n);
				if (p.z < minZ)
                {
					minZ = p.z;
					pointedImage = trackedImages[n];
				}
			}
		}

		if (pointedImage != null)
		{
			UpdateImages(pointedImage, true);
		}
	}

	private void Update()
	{
		UpdateViewedImages();

		// determine the name and transform of the currently tracked image
		string imageName;
		Transform imageTransform;
		if (trackedImagesOrder.Count > 0) // if actively tracking multiple images, use those images
		{
			imageName = trackedImagesOrder.Last();
			imageTransform = trackedImages[imageName].transform;
		}
		else if (limitedImagesOrder.Count > 0) // if not activately tracking an image, use the most recently tracked image
		{
			imageName = limitedImagesOrder.Last();
			imageTransform = trackedImages[imageName].transform;
		}
		else
		{
			if (currentForestScene != null)
			{
				currentForestScene.gameObject.SetActive(false);
			}
			return;
		}

		// check if the current image is too far, in which case hide the forest scene
		if (false)
        {
			currentForestScene.gameObject.SetActive(false);
		}
		// otherwise only perform changes if a different image is tracked since last update
		else if (currentImageName != imageName)
        {
			currentImageName = imageName;

			// activate the forest scene corresponding to the currently tracked image
			ForestScene newForestScene = imageToScene[imageName];
			if (newForestScene != currentForestScene)
			{
				if (currentForestScene != null)
				{
					currentForestScene.transform.parent = null;
					currentForestScene.gameObject.SetActive(false);
				}
				currentForestScene = newForestScene;
				currentForestScene.gameObject.SetActive(true);
			}

			// position and rotation for the AR image marker and its placeholder position within the current scene
			Quaternion sceneRotation = currentForestScene.imageToTransform[imageName].localRotation;
			Quaternion worldRotation = imageTransform.rotation;
			Vector3 scenePosition = currentForestScene.imageToTransform[imageName].localPosition;
			Vector3 worldPosition = imageTransform.position;

			Quaternion sceneToWorldRotation = worldRotation * Quaternion.Inverse(sceneRotation);

			Vector3 rotatedScenePosition = sceneToWorldRotation * scenePosition;

			currentForestScene.transform.position = worldPosition - rotatedScenePosition;
			currentForestScene.transform.rotation = sceneToWorldRotation;
			currentForestScene.transform.parent = imageTransform;
		}

		if (debugText != null)
		{
			debugText.text = "Tracked Images: " + string.Join(", ", trackedImagesOrder) + "\n Limited Images: " + string.Join(", ", limitedImagesOrder) + "\n Pointed Images: " + string.Join(", ", pointedImages) + "\n Current Image: " + imageName;
		}

		/*
		Dictionary<string, Transform> currentImages;
		if (trackedImages.Count > 0) // if actively tracking multiple images, use those images
		{
			currentImages = trackedImages;
		}
		else if (limitedImages.Count > 0) // if not activately tracking an image, use the most recently tracked image
		{
			currentImages = new Dictionary<string, Transform>() { { limitedImagesOrder[-1], limitedImages[limitedImagesOrder[-1]] } };
		}
		else
		{
			currentForestScene.gameObject.SetActive(false);
			return;
		}

		// average calculated position, up, and forward vectors for the scene's origin in world space
		Vector3 averagePosition = Vector3.zero;
		Vector3 averageForward = Vector3.zero;
		Vector3 averageUp = Vector3.zero;
		int numImages = 0;
		foreach (string imageName in currentImages.Keys)
		{
			if (!currentForestScene.imageToTransform.ContainsKey(imageName))
			{
				continue;
			}

			numImages += 1;
			// position and rotation for the AR image marker and its placeholder position within the current scene
			Quaternion sceneRotation = currentForestScene.imageToTransform[imageName].localRotation;
			Quaternion worldRotation = currentImages[imageName].transform.rotation;
			Vector3 scenePosition = currentForestScene.imageToTransform[imageName].localPosition;
			Vector3 worldPosition = currentImages[imageName].transform.position;

			Quaternion sceneToWorldRotation = worldRotation * Quaternion.Inverse(sceneRotation);
			averageForward += (sceneToWorldRotation * Vector3.forward).normalized;
			averageUp += (sceneToWorldRotation * Vector3.up).normalized;

			Vector3 rotatedScenePosition = sceneToWorldRotation * scenePosition;
			averagePosition += worldPosition - rotatedScenePosition;
		}

		if (numImages > 0)
		{
			averagePosition = averagePosition / numImages;
			averageForward = averageForward.normalized;
			averageUp = averageUp.normalized;
			Quaternion averageRotation = Quaternion.LookRotation(averageForward, averageUp);
			// currentForestScene.MoveTo(averagePosition, averageRotation);
			currentForestScene.transform.position = averagePosition;
			currentForestScene.transform.rotation = averageRotation;
			currentForestScene.gameObject.SetActive(true);
		}
		else
		{
			currentForestScene.gameObject.SetActive(false);
		}
		*/
	}
}
