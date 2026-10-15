using UnityEngine;
public sealed class BehaviorTestCamera : MonoBehaviour
{
    [Tooltip("Optional starting pose, sampled once when the scene starts.")]
    public Transform startReference;
    public Transform follow;
    public float speed=18;
    float yaw,pitch;
    bool wasFollowing;
    void Start()
    {
        if(startReference)transform.SetPositionAndRotation(startReference.position,startReference.rotation);
        follow=null;
        ReadAngles();
    }
    void ReadAngles()
    {
        yaw=transform.eulerAngles.y;
        pitch=Mathf.DeltaAngle(0,transform.eulerAngles.x);
    }
    void LateUpdate()
    {
        bool freeInput=Input.GetMouseButton(1);
        // Following is opt-in; taking camera control immediately returns to free flight.
        if(freeInput&&follow)follow=null;
        if(follow)
        {
            transform.position=follow.position+new Vector3(10,7,-12);
            transform.LookAt(follow.position+Vector3.up);
            wasFollowing=true;
            return;
        }
        if(wasFollowing){ReadAngles();wasFollowing=false;}
        if(!freeInput)return;
        yaw+=Input.GetAxisRaw("Mouse X")*2;pitch=Mathf.Clamp(pitch-Input.GetAxisRaw("Mouse Y")*2,-80,80);
        transform.rotation=Quaternion.Euler(pitch,yaw,0);
        transform.position+=(transform.forward*Input.GetAxisRaw("Vertical")+transform.right*Input.GetAxisRaw("Horizontal")+Vector3.up*((Input.GetKey(KeyCode.E)?1:0)-(Input.GetKey(KeyCode.Q)?1:0)))*speed*Time.unscaledDeltaTime*(Input.GetKey(KeyCode.LeftShift)?4:1);
    }
}
