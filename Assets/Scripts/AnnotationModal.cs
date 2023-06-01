using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

[RequireComponent(typeof(AudioSource))]
public class AnnotationModal : MonoBehaviour
{

    [SerializeField] private string defaultPlaybackTimeText = "#:##";
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text description;
    [SerializeField] private Transform imageGallery;
    [SerializeField] private Button close;
    [SerializeField] private GameObject annotationImagePrefab;
    [SerializeField] private AnnotationData testData;


    [SerializeField] private Button playPausePlaybackButton;
    [SerializeField] private Slider playbackSlider;
    [SerializeField] private TMP_Text currentPlaybackTimeText;
    [SerializeField] private TMP_Text totalPlaybackTimeText;

    [Header("Button Swappable Images")]
    [SerializeField] private Sprite playPlaybackGraphic;

    [SerializeField] private Sprite pausePlaybackGraphic;

    private AudioSource audioSource;
    private AudioClip audioClip;
    private float clipStartTime;


    private Image playPausePlaybackImage;
    // Start is called before the first frame update
    void Start()
    {
        playPausePlaybackImage = playPausePlaybackButton.gameObject.GetComponent<Image>();
        audioSource = gameObject.GetComponent<AudioSource>();

        close.onClick.AddListener(OnClose);
        playPausePlaybackButton.onClick.AddListener(PlayPausePlayback);
        OnClose();
        if (testData != null)
        {
            OpenModal(testData);
        }
    }

    private void PlayPausePlayback()
    {
        if (audioSource != null && audioClip != null)
        {
            if (audioSource.clip != audioClip)
            {
                audioSource.clip = audioClip;
            }

            if (audioSource.isPlaying)
            {
                audioSource.Pause();
            }
            else if (audioSource.time == 0)
            {
                playbackSlider.value = 0;
                audioSource.Play();
            }
            else
            {
                audioSource.UnPause();
            }
        }
    }

    private void SetPlaybackTime(float time)
    {
        if (time >= audioSource.clip.length)
        {
            audioSource.time = 0;
        }
        else
        {
            audioSource.time = time;
        }
    }

    private void Update()
    {
        if (audioClip == null)
        {
            // Slider
            playbackSlider.value = 0;
            playbackSlider.interactable = false;
            currentPlaybackTimeText.text = defaultPlaybackTimeText;
            totalPlaybackTimeText.text = defaultPlaybackTimeText;

            // Play/Pause button
            playPausePlaybackButton.interactable = false;
            playPausePlaybackImage.sprite = playPlaybackGraphic;

        }
        else
        {
            // slider
            if (playbackSlider.maxValue != audioClip.length)
                playbackSlider.maxValue = audioClip.length;

            currentPlaybackTimeText.text = FormatTime(audioSource.time);
            totalPlaybackTimeText.text = FormatTime(audioClip.length);

            // Play/Pause button and slider
            playPausePlaybackButton.interactable = true;
            if (audioSource.isPlaying)
            {
                if (playbackSlider.interactable)
                {
                    playbackSlider.interactable = false;
                    playPausePlaybackImage.sprite = pausePlaybackGraphic;
                    playbackSlider.onValueChanged.RemoveAllListeners();
                }
                playbackSlider.value = audioSource.time;
            }
            else
            {
                playPausePlaybackImage.sprite = playPlaybackGraphic;

                if (!playbackSlider.interactable)
                {
                    playbackSlider.value = audioSource.time;
                    playbackSlider.interactable = true;
                    playbackSlider.onValueChanged.AddListener(SetPlaybackTime);
                }
            }


        }
    }

    private string FormatTime(float time) // in seconds
    {
        int minutes = Mathf.FloorToInt(time / 60);
        int seconds = Mathf.FloorToInt(time % 60);

        string str = string.Format("{0:0}:{1:00}", minutes, seconds);

        return str;
    }

    private void OnClose()
    {
        gameObject.SetActive(false);
    }

    public void OpenModal(AnnotationData annotationData)
    {
        foreach (Transform child in imageGallery.transform)
        {
            Destroy(child.gameObject);
        }

        title.text = annotationData.title;
        description.text = annotationData.description;
        audioClip = annotationData.audio;

        foreach (Sprite sprite in annotationData.images)
        {
            GameObject annotationImage = Instantiate(annotationImagePrefab, imageGallery);
            Image img = annotationImage.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            
        }
        gameObject.SetActive(true);
    }
}
