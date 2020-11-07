using UnityEngine;
using System.Collections.Generic;

public class PlayerSyncStructs{
    public struct Inputs
    {
        public int submitButon;
        public Vector2 stickMove;
        public Vector3 mousePos;

    }

    public struct InputMessage
    {
        public float delivery_time;
        public uint start_tick_number;
        public Vector3 position;
        public List<Inputs> inputs;
    }

    public struct ClientState
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;
    }

    public struct StateMessage
    {
        public float delivery_time;
        public uint tick_number;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;
        public Vector3 angular_velocity;
    }

}