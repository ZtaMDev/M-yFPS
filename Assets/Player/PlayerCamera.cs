using UnityEngine;

public class PlayerCamera : MonoBehaviour
{
    public float sensitivity = 2f;
    public Transform playerBody;
    private float verticalRotation = 0f;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        float mouseX = Input.GetAxis("Mouse X") * sensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * sensitivity;

        verticalRotation -= mouseY;
        verticalRotation = Mathf.Clamp(verticalRotation, -60f, 60f);

        transform.localRotation = Quaternion.Euler(verticalRotation, 0, 0);
        playerBody.Rotate(Vector3.up * mouseX);
    }
}
