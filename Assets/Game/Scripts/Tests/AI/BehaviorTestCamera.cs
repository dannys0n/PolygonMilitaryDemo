using UnityEngine;
public sealed class BehaviorTestCamera : MonoBehaviour
{
    public Transform follow;
    public float speed=18;
    float yaw,pitch;
    void Start(){yaw=transform.eulerAngles.y;pitch=transform.eulerAngles.x;}
    void LateUpdate()
    {
        if(follow){transform.position=follow.position+new Vector3(10,7,-12);transform.LookAt(follow.position+Vector3.up);return;}
        if(!Input.GetMouseButton(1))return;
        yaw+=Input.GetAxisRaw("Mouse X")*2;pitch=Mathf.Clamp(pitch-Input.GetAxisRaw("Mouse Y")*2,-80,80);
        transform.rotation=Quaternion.Euler(pitch,yaw,0);
        transform.position+=(transform.forward*Input.GetAxisRaw("Vertical")+transform.right*Input.GetAxisRaw("Horizontal")+Vector3.up*((Input.GetKey(KeyCode.E)?1:0)-(Input.GetKey(KeyCode.Q)?1:0)))*speed*Time.unscaledDeltaTime*(Input.GetKey(KeyCode.LeftShift)?4:1);
    }
}
