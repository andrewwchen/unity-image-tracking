using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Clock : MonoBehaviour
{
    public GameObject secondHand;
    public GameObject minuteHand;
    public GameObject hourHand;
    public GameObject pendulum;
    public float startHour = 0;
    public float realMinutesPerHour = 60;
    public bool shouldRound = true;
    public int roundToNearestDegree = 6;
    public int roundDampingPower = 16;
    public float pendulumMaxAngle = 12;
    public AudioClip tickingSound;

    private AudioSource audioSource;


    private float realStartTime = 0;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.clip = tickingSound;
    }

    // Start is called before the first frame update
    void OnEnable()
    {
        realStartTime = Time.time;
        audioSource.Play();
    }

    // Update is called once per frame
    void Update()
    {
        float realSecondsPerHour = realMinutesPerHour * 60;
        float currentTime = Time.time - realStartTime;
        float hours = currentTime / realSecondsPerHour + startHour;
        float minutes = hours * 60f;
        float seconds = minutes * 60f;

        float hourAngle = hours / 12f * 360f;
        float minuteAngle = minutes / 60f * 360f;
        float secondAngle = seconds / 60f * 360f;
        if (shouldRound)
        {
            hourAngle =   Mathf.Floor(hourAngle   / roundToNearestDegree) * roundToNearestDegree + Mathf.Pow(hourAngle   / roundToNearestDegree - Mathf.Floor(hourAngle   / roundToNearestDegree), roundDampingPower);
            minuteAngle = Mathf.Floor(minuteAngle / roundToNearestDegree) * roundToNearestDegree + Mathf.Pow(minuteAngle / roundToNearestDegree - Mathf.Floor(minuteAngle / roundToNearestDegree), roundDampingPower);
            secondAngle = Mathf.Floor(secondAngle / roundToNearestDegree) * roundToNearestDegree + Mathf.Pow(secondAngle / roundToNearestDegree - Mathf.Floor(secondAngle / roundToNearestDegree), roundDampingPower);
        }

        hourHand.transform.localEulerAngles = Vector3.right * hourAngle;
        minuteHand.transform.localEulerAngles = Vector3.right* minuteAngle;
        secondHand.transform.localEulerAngles = Vector3.right* secondAngle;
        pendulum.transform.localEulerAngles = Vector3.right * Mathf.Sin(seconds * 2) * 12f;

    }
}
