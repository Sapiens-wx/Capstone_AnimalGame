using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.Animals.Communication
{
    public enum ContactId { Lin, Mara, CrewLounge }
    public enum MessageKind { Text, Photos }

    [Serializable]
    public sealed class ContactDefinition
    {
        public ContactId contact;
        public string profession;
        public Sprite portrait;
        public string Name => ContactName(contact);
        public static string ContactName(ContactId id) => id == ContactId.CrewLounge ? "Crew lounge" : id.ToString();
    }

    [CreateAssetMenu(menuName = "Animal Game/Communication/Contacts")]
    public sealed class Contacts : ScriptableObject
    {
        public List<ContactDefinition> contacts = new();
    }

    [Serializable]
    public sealed class ContactConversation
    {
        public ContactId contact;
        public List<ChatMessage> messages = new();
    }

    [Serializable]
    public sealed class ChatMessage
    {
        public bool fromPlayer;
        public int hour, minute;
        public MessageKind kind;
        public string text;
        public int[] photoIndices = Array.Empty<int>();
    }
}