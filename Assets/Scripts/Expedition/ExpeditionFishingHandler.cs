using System;
using System.Collections;
using System.Linq;
using System.Numerics;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Quaternion = UnityEngine.Quaternion;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

namespace DIPProject
{
    public class ExpeditionFishingHandler : MonoBehaviourPunCallbacks, IOnEventCallback
    {
        #region Variables

        #region Time
        /// <summary>
        ///     Time are always in seconds.
        /// </summary>
        private TimeSpan _totalTime;
        private TimeSpan _timerTime;

        private TimeSpan TotalTime
        {
            get => _totalTime;
            set
            {
                _totalTime = value;
                totalTimeText.text = TotalTimeAsString();
                TimerTime = _totalTime;
            }
        }

        private TimeSpan TimerTime
        {
            get => _timerTime;
            set
            {
                _timerTime = value;
                timerTimeText.text = TimerTimeAsString();
            }
        }
        
        public bool isTimerRunning;

        #endregion

        [Tooltip("These are the locations that the players will be teleported to, x, y")] 
        public Vector2[] fishingTeleportLocations;
        // These are the locations that the players were before teleporting. 
        private Vector2[] fishingTeleportLocationsBefore = new Vector2[CreateRoomHandler.MAX_PLAYERS];
        
        [Tooltip("These Buttons will be disabled if the person is not the host.")]
        public Selectable[] nonHostSelectableDisable;

        [Tooltip("The slider to adjust duration of the expedition")]
        public Slider totalTimeSlider;

        [Tooltip("The total timer on the bottom-right")]
        public TMP_Text totalTimeText;

        [Tooltip("The on-screen timer that pops up on start fishing")]
        public TMP_Text timerTimeText;

        [Tooltip("This is the formatting for the timer.")] [SerializeField]
        public const string TIMER_FORMAT = @"hh\:mm\:ss";

        [Tooltip("This is to adjust how fast time flows. This is mainly used for debugging.")] [SerializeField]
        public uint DEBUG_TIME_MULTIPLIER = 1;

        [Tooltip("This is used to trigger the start/end expedition.")]
        public Animator animator;

        #region Event Codes

        private const byte SyncTimerTimeEventCode = 1;
        private const byte SyncTotalTimeEventCode = 2;

        private const byte SyncStartEventCode = 3;
        private const byte SyncEndEventCode = 4;

        #endregion

        #endregion

        #region Sync Event Callbacks

        public void OnEvent(EventData photonEvent)
        {
            switch (photonEvent.Code)
            {
                case SyncStartEventCode:
                    StartExpeditionChild();
                    break;

                case SyncEndEventCode:
                    EndExpeditionChild();
                    break;

                case SyncTimerTimeEventCode:
                    LoopExpeditionChild(TimeSpan.FromSeconds((double) photonEvent.CustomData));
                    break;

                case SyncTotalTimeEventCode:
                    // This will yield the slider value made by the host.
                    // Which in turn will trigger appropriate updates of time
                    totalTimeSlider.value = (float) photonEvent.CustomData;
                    break;
            }
        }

        #endregion

        #region Host UI Handler

        /// <summary>
        ///     Disables host settings for those who are not host.
        ///     Enables those for host.
        /// </summary>
        private void ValidateHostButtonsUI()
        {
            foreach (var obj in nonHostSelectableDisable) obj.interactable = PhotonNetwork.IsMasterClient;
        }

        #endregion

        #region MonoBehaviorPunCallbacks Callbacks

        public override void OnPlayerLeftRoom(Player otherPlayer)
        {
            ValidateHostButtonsUI();
            base.OnPlayerLeftRoom(otherPlayer);
        }

        public override void OnJoinedRoom()
        {
            ValidateHostButtonsUI();
            base.OnJoinedRoom();
        }

        public override void OnPlayerEnteredRoom(Player newPlayer)
        {
            SyncTimerTimeEvent();
            SyncTotalTimeEvent();
            base.OnPlayerEnteredRoom(newPlayer);
        }

        #endregion

        #region Expedition Timer Loop

        /// In the Expedition Timer Loop, the host is the main controller
        /// That means, we will NOT call coroutines on participants.
        /// This will ensure that the synchronization is only based on the host.
        /// <summary>
        ///     This is a host-only function.
        ///     This means that this code will not be executed on participants.
        /// </summary>
        public void StartExpeditionHost()
        {
            if (!isTimerRunning)
            {
                isTimerRunning = true;
                SyncStartEvent();
                StartCoroutine(LoopExpeditionHost());
            }
        }

        private void StartExpeditionChild()
        {
            Debug.Log("Expedition Timer has started at " + TotalTime);
            // Though called at Host, the child will need to affirm this running variable too
            isTimerRunning = true;

            // Just in case it's not synced.
            FreezePlayers();
            animator.SetTrigger("Start Expedition");
        }

        /// <summary>
        ///     This loops through the expedition timer.
        ///     The host will regularly update the participants on the Timer time via Syncing.
        /// </summary>
        /// <returns></returns>
        private IEnumerator LoopExpeditionHost()
        {
            // Waits for 1 second, if we have a DEBUG_TIME_MULTIPLIER, then it'll be faster.
            yield return new WaitForSeconds(1 / DEBUG_TIME_MULTIPLIER);
            _timerTime -= TimeSpan.FromSeconds(1);

            // While the Timer time is still positive, we loop the Coroutine until it isn't.
            if (TimerTime.TotalSeconds >= 0)
            {
                // Sync with participants
                SyncTimerTimeEvent();

                // This simply calls itself (thus looping) if it's not done.
                StartCoroutine(LoopExpeditionHost());
            }
            else
            {
                EndExpeditionHost();
            }
        }

        private void LoopExpeditionChild(TimeSpan timerTime)
        {
            TimerTime = timerTime;
            if (TimerTime.Seconds % 10 == 0) Debug.Log("Expedition Timer at " + TimerTimeAsString());
        }


        /// <summary>
        ///     This means that the host has detected the end of the expedition.
        ///     The host will now tell all the participants.
        /// </summary>
        private void EndExpeditionHost()
        {
            // Tells participants that the event has ended, will trigger unfreeze
            SyncEndEvent();
        }

        private void EndExpeditionChild()
        {
            Debug.Log("Expedition has ended! Total Time " + TotalTimeAsString());
            isTimerRunning = false;
            UnfreezePlayers();
            TimerTime = TotalTime;
            animator.SetTrigger("End Expedition");
        }

        #endregion

        #region Sync Events

        /// <summary>
        ///     This helps sync the total time, this happens when the host changes the total time value using settings.
        /// </summary>
        private void SyncTotalTimeEvent()
        {
            if (!PhotonNetwork.IsMasterClient) return;
            var raiseEventOptions = new RaiseEventOptions {Receivers = ReceiverGroup.Others};
            PhotonNetwork.RaiseEvent(
                SyncTotalTimeEventCode,
                totalTimeSlider.value, // Content
                raiseEventOptions,
                SendOptions.SendReliable
            );
        }

        /// <summary>
        ///     This helps sync everyone's timer.
        /// </summary>
        private void SyncTimerTimeEvent()
        {
            if (!PhotonNetwork.IsMasterClient) return;
            var raiseEventOptions = new RaiseEventOptions {Receivers = ReceiverGroup.All};
            PhotonNetwork.RaiseEvent(
                SyncTimerTimeEventCode,
                TimerTime.TotalSeconds, // Content
                raiseEventOptions,
                SendOptions.SendReliable
            );
        }

        /// <summary>
        ///     This occurs when the host starts the timer
        /// </summary>
        private void SyncStartEvent()
        {
            if (!PhotonNetwork.IsMasterClient) return;
            var raiseEventOptions = new RaiseEventOptions {Receivers = ReceiverGroup.All};
            PhotonNetwork.RaiseEvent(SyncStartEventCode, null, raiseEventOptions, SendOptions.SendReliable);
        }

        /// <summary>
        ///     This occurs when the host has finished the timer
        /// </summary>
        private void SyncEndEvent()
        {
            if (!PhotonNetwork.IsMasterClient) return;
            var raiseEventOptions = new RaiseEventOptions {Receivers = ReceiverGroup.All};
            PhotonNetwork.RaiseEvent(SyncEndEventCode, null, raiseEventOptions, SendOptions.SendReliable);
        }

        #endregion

        #region MonoBehavior Callbacks

        /// <summary>
        ///     This simply adds this instance as a Event Callback participant, so OnEvent will be triggered.
        /// </summary>
        private void OnEnable()
        {
            PhotonNetwork.AddCallbackTarget(this);
        }

        /// <summary>
        ///     This removes the OnEvent Callback
        /// </summary>
        private void OnDisable()
        {
            PhotonNetwork.RemoveCallbackTarget(this);
        }

        private void Start()
        {
            TotalTime = TimeSpan.FromMinutes(totalTimeSlider.value);
            TimerTime = TotalTime;
        }

        #endregion

        #region Formatting Methods

        /// <summary>
        ///     Gets the Timer time in hh:mm:ss Format
        /// </summary>
        private string TimerTimeAsString()
        {
            return TimerTime.ToString(TIMER_FORMAT);
        }

        /// <summary>
        ///     Gets the Total time in hh:mm:ss Format
        /// </summary>
        private string TotalTimeAsString()
        {
            return TotalTime.ToString(TIMER_FORMAT);
        }

        /// <summary>
        ///     Updates the timer text on slider change
        /// </summary>
        public void UpdateTotalTimeText()
        {
            TotalTime = TimeSpan.FromMinutes(totalTimeSlider.value);
            SyncTotalTimeEvent();
        }

        #endregion

        #region Player Movement Methods

        private void TeleportPlayersTo()
        {
            var players = GetPlayers();
            for (int i = 0; i < players.Length; i++)
            {
                players[i].transform.SetPositionAndRotation(
                    new Vector3(fishingTeleportLocations[i].x, fishingTeleportLocations[i].y, this.transform.position.z),
                    Quaternion.identity
                    );
                fishingTeleportLocationsBefore.Append(
                    new Vector2(players[i].transform.position.x, players[i].transform.position.y)
                    );
            }
        }
        private void TeleportPlayersBack()
        {
            var players = GetPlayers();
            for (int i = 0; i < players.Length; i++)
            {
                players[i].transform.SetPositionAndRotation(
                    new Vector3(fishingTeleportLocations[i].x, fishingTeleportLocations[i].y,0),
                    Quaternion.identity
                    );
                fishingTeleportLocationsBefore[i].x = players[i].transform.position.x;
                fishingTeleportLocationsBefore[i].y = players[i].transform.position.y;
            }
        }
        
        private void FreezePlayers()
        {
            var players = GetPlayers();
            players[0].GetComponent<Rigidbody2D>().constraints |=
                RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezePositionY;
        }

        public void UnfreezePlayers()
        {
            var players = GetPlayers();
            players[0].GetComponent<Rigidbody2D>().constraints ^=
                RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezePositionY;
        }

        private GameObject[] GetPlayers()
        {
            return GameObject.FindGameObjectsWithTag("Player");
        }
        
        #endregion
    }
}