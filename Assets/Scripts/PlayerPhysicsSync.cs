using UnityEngine;
using System.Collections.Generic;
using Photon.Pun;
using System.IO;
using Photon.Realtime;
using ExitGames.Client.Photon;

// Based on code by Joe Best-Rotheray: https://github.com/spectre1989/unity_physics_csp/blob/master/Assets/Logic.cs

public class PlayerPhysicsSync : MonoBehaviourPun
{
    public float smoothing_speed = 7f;

    // client specific
    private float client_timer;
    private uint client_tick_number;
    private uint client_last_received_state_tick;
    private uint client_last_sent_state_tick;
    private const int c_client_buffer_size = 1024;
    private PlayerSyncStructs.ClientState[] client_state_buffer; // client stores predicted moves here
    private PlayerSyncStructs.Inputs[] client_input_buffer; // client stores predicted inputs here
    private Queue<PlayerSyncStructs.StateMessage> client_state_msgs;
    private Vector3 client_pos_error;
    private Quaternion client_rot_error;
    public GameObject playerPrefab;
    private Rigidbody proxy_client_player;
    private float start_smooth_speed = 0;

    private float server_lag = 0;

    private uint client_tick_accumulator;
    public Queue<uint> client_last_input_tick;

    private Queue<PlayerSyncStructs.InputMessage> queue_input_msgs;


    // server specific
    public uint server_snapshot_rate;
    private uint server_tick_number;
    private uint server_tick_accumulator;
    private Queue<PlayerSyncStructs.InputMessage> server_input_msgs;
    private float client_input_lag = 0;

    private void Start()
    {
        this.client_timer = 0.0f;
        this.client_tick_number = 0;
        this.client_last_received_state_tick = 0;
        this.client_state_buffer = new PlayerSyncStructs.ClientState[c_client_buffer_size];
        this.client_input_buffer = new PlayerSyncStructs.Inputs[c_client_buffer_size];
        this.client_state_msgs = new Queue<PlayerSyncStructs.StateMessage>();
        this.queue_input_msgs = new Queue<PlayerSyncStructs.InputMessage>();
        this.client_last_input_tick = new Queue<uint>();

        this.client_pos_error = Vector3.zero;
        this.client_rot_error = Quaternion.identity;

        this.server_tick_number = 0;
        this.server_tick_accumulator = 0;
        this.server_input_msgs = new Queue<PlayerSyncStructs.InputMessage>();

        start_smooth_speed = smoothing_speed;

        Application.targetFrameRate = 100; //limit frame rate to 100 when in online so we don't overdue our message limits since the snapshot rate is related to frame rate
        // THIS CAN AND SHOULD BE FRAME INDEPENDENT TO REMOVE THIS RESTRICTION OTHERWISE YOU WILL OVERUSE YOUR NETWORK BANDWITH
    }


    private void Update()
    {
        //Set the server rate based on frames
        if (this.photonView.IsMine)
        {
            float deltaTimeFPS = 0;
            deltaTimeFPS += Time.deltaTime;
            deltaTimeFPS /= 2.0f;
            int fps = (int)(1.0f / deltaTimeFPS);

            // UPDATE OUR SERVER/HOST CLIENTS RATE AT WHICH WE ARE SENDING PHYSICS SNAPSHOTS BASED ON THEIR FRAME RATE OR THE PLAYER COUNT
            // We do this to limit the amount of info we send so we don't overdo network traffic. (This should be adjusted for your game and network capabilities)
            //TODO
            // server_snapshot_rate = (uint)Mathf.Max((int)(fps / 20), NetworkInputManager.instance.GetPlayerCount() * 2); EXAMPLE
            server_snapshot_rate = (uint)(fps / 20);
        }

        //If we have not yet spawned a Proxy Player (represents the position of the networked player), spawn it
        if (proxy_client_player == null)
        {
            GameObject temp = Instantiate(playerPrefab, Vector3.zero, Quaternion.identity);
            proxy_client_player = temp.GetComponent<Rigidbody>();
            proxy_client_player.position = transform.position;
        }

        /* CLIENT UPDATE - SEND INPUTS IF HOST OF THIS PLAYER / PERFORM CORRECTIONS IF ON ANOTHER CLIENT */
        /******************************************************/

        float dt = Time.fixedDeltaTime;
        // client update
        Rigidbody client_rigidbody = this.GetComponent<Rigidbody>();
        float client_timer = this.client_timer;
        uint client_tick_number = this.client_tick_number;


        if (this.photonView.IsMine)
        {
            uint buffer_slot = client_tick_number % c_client_buffer_size;

            // sample and store inputs for this tick
            // grab the inputs from network input controller
            PlayerSyncStructs.Inputs inputs = new PlayerSyncStructs.Inputs();

            /* TODO GRAB RELEVANT INPUT FOR YOUR GAME */
            // I have a custom controller componenet that is tracking when this player recieves input to send
            // You can use whatever method you use to capture input     

            /* EXAMPLE
            
            inputs.submitButon = NetworkInputManager.instance.GetMyController().GetSubmitButton();
            inputs.stickMove = NetworkInputManager.instance.GetMyController().GetStickMove();
            inputs.mousePos = NetworkInputManager.instance.GetMyController().GetMousePos();

            */
            inputs.submitButon = 1; // THIS IS JUST A PLACEHOLDER
            /* END TODO */

            this.client_input_buffer[buffer_slot] = inputs;

            // store state for this tick, then use current state + input to step simulation
            this.ClientStoreCurrentStateAndStep(
                ref this.client_state_buffer[buffer_slot],
                client_rigidbody,
                inputs,
                dt);

            if (inputs.submitButon == 1)
            {
                client_last_input_tick.Enqueue(client_tick_number);
                proxy_client_player.position = client_rigidbody.position;
                proxy_client_player.rotation = client_rigidbody.rotation;
                proxy_client_player.velocity = client_rigidbody.velocity;
                proxy_client_player.angularVelocity = client_rigidbody.angularVelocity;
            }


            //If we haven't passed the client snapshot rate just queue to send (Only send our input queue every 3rd frame to reduce traffic)
            //Otherwise send the queue
            //TODO UPDATE THIS CLIENT SNAPSHOT RATE BASED ON GAME AND NETWORK TRAFFIC LIMITS
            ++client_tick_accumulator;
            if (client_tick_accumulator > 2)
            {
                client_tick_accumulator = 0;

                // send input packet to server

                PlayerSyncStructs.InputMessage input_msg;
                input_msg.delivery_time = (float)PhotonNetwork.Time;
                input_msg.start_tick_number = client_tick_number - 3; //SEND THE LAST THREE FRAMES AS THIS MATCHES OUR THRESHOLD ABOVE (IF YOU ADJUST ONE, ADJUST THE OTHER)
                input_msg.position = client_rigidbody.position;
                input_msg.inputs = new List<PlayerSyncStructs.Inputs>();

                uint start_tick = input_msg.start_tick_number;

                if (client_tick_number - start_tick > 50)
                {
                    start_tick = client_tick_number;
                }

                for (uint tick = start_tick; tick <= client_tick_number; ++tick)
                {
                    input_msg.inputs.Add(this.client_input_buffer[tick % c_client_buffer_size]);
                }

                SendServerMessage(input_msg);
                this.client_last_sent_state_tick = client_tick_number;
            }

        }
        ++client_tick_number;


        if (this.ClientHasStateMessage())
        {
            PlayerSyncStructs.StateMessage recieved_state_msg = this.client_state_msgs.Dequeue();
            while (this.ClientHasStateMessage()) // make sure if there are any newer state messages available, we use those instead
            {
                recieved_state_msg = this.client_state_msgs.Dequeue();
            }

            this.client_last_received_state_tick = recieved_state_msg.tick_number;

            //Correct only if the newest message is newer than our last input and ten ticks (buffer in case of delay with large group with ping)

            if (client_last_input_tick.Count > 0)
                client_last_input_tick.Dequeue();

            uint buffer_slot = recieved_state_msg.tick_number % c_client_buffer_size;
            Vector3 position_error = recieved_state_msg.position - this.client_state_buffer[buffer_slot].position;
            float rotation_error = 1.0f - Quaternion.Dot(recieved_state_msg.rotation, this.client_state_buffer[buffer_slot].rotation);

            if ((position_error.sqrMagnitude > 0.2f ||
                rotation_error > 0.1f))
            {
                if (this.photonView.IsMine)
                    Debug.Log("Correcting for error at tick " + recieved_state_msg.tick_number + " (rewinding " + (client_tick_number - recieved_state_msg.tick_number) + " ticks)");

                // If the packet is valid update our network proxy
                if (!float.IsNaN(recieved_state_msg.position.x))
                {
                    proxy_client_player.position = Vector3.Lerp(proxy_client_player.position, recieved_state_msg.position, 0.5f);
                    proxy_client_player.rotation = recieved_state_msg.rotation.normalized;
                    proxy_client_player.velocity = recieved_state_msg.velocity;
                    proxy_client_player.angularVelocity = recieved_state_msg.angular_velocity;
                }

                // If the packet is valid update skew our visible players velociy to anticipate corrections
                if (!float.IsNaN(recieved_state_msg.velocity.x))
                {
                    client_rigidbody.velocity = Vector3.Slerp(client_rigidbody.velocity, recieved_state_msg.velocity, 0.3f);
                }

                // If we're past threshold, just snap
                // If we're going fast be less likely to snap
                if (!float.IsNaN(recieved_state_msg.position.x) && (recieved_state_msg.position - client_rigidbody.position).sqrMagnitude >= Mathf.Clamp(recieved_state_msg.velocity.magnitude, 1f, 4.5f))
                {
                    client_rigidbody.position = proxy_client_player.position;
                    client_rigidbody.rotation = proxy_client_player.rotation;
                    client_rigidbody.velocity = proxy_client_player.velocity;
                    client_rigidbody.angularVelocity = proxy_client_player.angularVelocity;
                }
                else if (!float.IsNaN(recieved_state_msg.position.x) && Mathf.Abs(recieved_state_msg.velocity.magnitude - client_rigidbody.velocity.magnitude) >= 6f)
                {
                    client_rigidbody.position = proxy_client_player.position;
                    client_rigidbody.rotation = proxy_client_player.rotation;
                    client_rigidbody.velocity = proxy_client_player.velocity;
                    client_rigidbody.angularVelocity = proxy_client_player.angularVelocity;
                }
            }
        }

        this.client_timer = client_timer;
        this.client_tick_number = client_tick_number;

        if (!this.photonView.IsMine)
        {
            if ((proxy_client_player.position - client_rigidbody.position).sqrMagnitude >= .1f)
            {
                client_rigidbody.position = Vector3.Lerp(client_rigidbody.position, proxy_client_player.position, dt * smoothing_speed);
            }
        }

        /* SERVER UPDATE - SEND UPDATES IF HOST OF THIS PLAYER / PERFORM INPUTS IF ON ANOTHER CLIENT */
        /******************************************************/

        uint server_tick_number = this.server_tick_number;
        uint server_tick_accumulator = this.server_tick_accumulator;
        Rigidbody server_rigidbody = this.GetComponent<Rigidbody>();

        PlayerSyncStructs.StateMessage state_msg;

        while (this.server_input_msgs.Count > 0 && (float)PhotonNetwork.Time >= this.server_input_msgs.Peek().delivery_time)
        {
            PlayerSyncStructs.InputMessage input_msg = this.server_input_msgs.Dequeue();

            // message contains an array of inputs, calculate what tick the final one is
            uint max_tick = input_msg.start_tick_number + (uint)input_msg.inputs.Count - 1;

            // if that tick is greater than or equal to the current tick we're on, then it
            // has inputs which are new
            if (max_tick >= server_tick_number)
            {
                // there may be some inputs in the array that we've already had,
                // so figure out where to start
                uint start_i = server_tick_number > input_msg.start_tick_number ? (server_tick_number - input_msg.start_tick_number) : 0;

                // run through all relevant inputs, and step player forward
                for (int i = (int)start_i; i < input_msg.inputs.Count; ++i)
                {
                    //If we are host of player, don't simulate our player physics as we already did this
                    //But we still want to send state to other clients
                    if (!this.photonView.IsMine)
                    {
                        Move(input_msg.inputs[i].stickMove);

                        if (input_msg.inputs[i].submitButon == 1)
                        {
                            // OPTIONAL: adjust our player's position by 1/4th so that the input is more accurate
                            // Can cause visible lag jump (teleporting player) if players are moving fast
                            server_rigidbody.position = Vector3.Lerp(server_rigidbody.position, input_msg.position, 0.25f);
                            SubmitButtonApply(input_msg.inputs[i].mousePos, input_msg.inputs[i].stickMove, server_rigidbody);
                        }
                    }

                    ++server_tick_number;
                    ++server_tick_accumulator;
                    if (this.photonView.IsMine)
                    {
                        if (server_tick_accumulator >= this.server_snapshot_rate)
                        {
                            server_tick_accumulator = 0;

                            state_msg.delivery_time = (float)PhotonNetwork.Time;
                            state_msg.tick_number = server_tick_number;
                            state_msg.position = server_rigidbody.position;
                            state_msg.rotation = server_rigidbody.rotation.normalized;
                            state_msg.velocity = server_rigidbody.velocity;
                            state_msg.angular_velocity = server_rigidbody.angularVelocity;
                            SendClientMessage(state_msg.delivery_time, (int)state_msg.tick_number, state_msg.position, state_msg.rotation, state_msg.velocity, state_msg.angular_velocity);
                        }
                    }
                }

                proxy_client_player.position = server_rigidbody.position;
                proxy_client_player.rotation = server_rigidbody.rotation;
            }
        }

        // If we're the host and master player send our state since we don't recieve messages from ourself
        if (this.photonView.IsMine)
        {
            ++server_tick_number;
            ++server_tick_accumulator;

            if (server_tick_accumulator >= this.server_snapshot_rate)
            {
                server_tick_accumulator = 0;
                state_msg.delivery_time = (float)PhotonNetwork.Time;
                state_msg.tick_number = server_tick_number;
                state_msg.position = server_rigidbody.position;
                state_msg.rotation = server_rigidbody.rotation.normalized;
                state_msg.velocity = server_rigidbody.velocity;
                state_msg.angular_velocity = server_rigidbody.angularVelocity;
                SendClientMessage(state_msg.delivery_time, (int)state_msg.tick_number, state_msg.position, state_msg.rotation, state_msg.velocity, state_msg.angular_velocity);
            }
        }

        this.server_tick_number = server_tick_number;
        this.server_tick_accumulator = server_tick_accumulator;
    }

    private System.Collections.IEnumerator ReturnToSmooth()
    {
        yield return new WaitForSeconds(0.3f);
        smoothing_speed = start_smooth_speed;
    }
    private void OnDisable()
    {
        if (proxy_client_player != null)
            Destroy(proxy_client_player);
    }

    private void SendServerMessage(PlayerSyncStructs.InputMessage client_input_msg)
    {
        double deliveryTime = (double)client_input_msg.delivery_time;
        int startTick = (int)client_input_msg.start_tick_number;

        //We will save all inputs in long lists that we will index for diff messages using inputsLength
        List<int> inputListSubmit = new List<int>();
        List<Vector2> inputListStick = new List<Vector2>();
        List<Vector3> inputListMouse = new List<Vector3>();

        for (int i = 0; i < client_input_msg.inputs.Count; i++)
        {

            //Save the length of the inputlist to expect when recieving
            //This lets us find at which point in the lists a new message begins
            List<PlayerSyncStructs.Inputs> inputList = client_input_msg.inputs;

            foreach (PlayerSyncStructs.Inputs input in inputList)
            {
                inputListSubmit.Add(input.submitButon);
                inputListStick.Add(input.stickMove);
                inputListMouse.Add(input.mousePos);
            }

        }

        this.photonView.RPC("SendServerMessageRPC", RpcTarget.Others, deliveryTime, startTick, client_input_msg.position, (object)(inputListSubmit.ToArray()), (object)(inputListStick.ToArray()), (object)(inputListMouse.ToArray()));
    }


    [PunRPC]
    void SendServerMessageRPC(double deliveryTime, int startTick, Vector3 pos, int[] inputListSubmit, Vector2[] inputListStick, Vector3[] inputListMouse, PhotonMessageInfo info)
    {
        List<PlayerSyncStructs.Inputs> inputList = new List<PlayerSyncStructs.Inputs>();
        for (int i = 0; i < inputListSubmit.Length; i++)
        {
            PlayerSyncStructs.Inputs new_input = new PlayerSyncStructs.Inputs();
            new_input.submitButon = inputListSubmit[i];
            new_input.stickMove = inputListStick[i];
            new_input.mousePos = inputListMouse[i];

            inputList.Add(new_input);
        }


        PlayerSyncStructs.InputMessage input_msg;
        input_msg.delivery_time = (float)deliveryTime;
        input_msg.start_tick_number = (uint)startTick;
        input_msg.position = pos;
        input_msg.inputs = inputList;
        client_input_lag = Mathf.Abs((float)(PhotonNetwork.Time - info.SentServerTime));
        if (this.server_input_msgs != null)
            this.server_input_msgs.Enqueue(input_msg);
    }

    private void SendClientMessage(float deliveryTime, int serverTick, Vector3 pos, Quaternion rot, Vector3 vel, Vector3 angVel)
    {
        this.photonView.RPC("SendClientMessageRPC", RpcTarget.Others, (double)deliveryTime, serverTick, pos, rot, vel, angVel);
    }


    [PunRPC]
    void SendClientMessageRPC(double deliveryTime, int serverTick, Vector3 pos, Quaternion rot, Vector3 vel, Vector3 angVel, PhotonMessageInfo info)
    {
        PlayerSyncStructs.StateMessage state_msg;
        state_msg.delivery_time = (float)deliveryTime;
        state_msg.tick_number = (uint)serverTick;
        state_msg.position = pos;
        state_msg.rotation = rot;
        state_msg.velocity = vel;
        state_msg.angular_velocity = angVel;
        server_lag = Mathf.Abs((float)(PhotonNetwork.Time - info.SentServerTime));
        if (this.client_state_msgs != null)
            this.client_state_msgs.Enqueue(state_msg);
    }
    private void SwapPhysics(Rigidbody moving, Rigidbody target)
    {
        Vector3 position = moving.position;
        Vector3 velocity = moving.velocity;
        Vector3 angVelocity = moving.angularVelocity;
        Quaternion rotation = moving.rotation;

        moving.position = target.position;
        moving.rotation = target.rotation;
        moving.velocity = target.velocity;
        moving.angularVelocity = target.angularVelocity;

        target.position = position;
        target.rotation = rotation;
        target.velocity = velocity;
        target.angularVelocity = angVelocity;
    }

    private bool ClientHasStateMessage()
    {
        return this.client_state_msgs.Count > 0 && (float)PhotonNetwork.Time >= this.client_state_msgs.Peek().delivery_time;
    }

    private void ClientStoreCurrentStateAndStep(ref PlayerSyncStructs.ClientState current_state, Rigidbody rigidbody, PlayerSyncStructs.Inputs inputs, float dt)
    {
        current_state.position = rigidbody.position;
        current_state.rotation = rigidbody.rotation;

        Move(inputs.stickMove);

        if (inputs.submitButon == 1)
        {
            SubmitButtonApply(inputs.mousePos, inputs.stickMove, rigidbody);
        }
    }

    //Called to perform submit button physics
    private void SubmitButtonApply(Vector3 mousePos, Vector2 sitckMove, Rigidbody rigidbody)
    {
        // TODO PERFORM YOUR PLAYERS INPUT SUBMIT
    }

    private void Move(Vector2 stickMove)
    {
        // TODO PERFORM YOUR PLAYERS INPUT MOVE
    }

    [PunRPC]
    void SendCollisionRPC(Vector3 pos, Vector3 velocity, PhotonMessageInfo info)
    {
        if (!photonView.IsMine)
        {
            Rigidbody client_rigidbody = this.GetComponent<Rigidbody>();

            client_rigidbody.velocity = velocity;
            client_rigidbody.position = pos + (velocity * Mathf.Abs((float)(PhotonNetwork.Time - info.SentServerTime)));
            this.proxy_client_player.position = client_rigidbody.position;
            this.proxy_client_player.velocity = client_rigidbody.velocity;
        }
    }

}