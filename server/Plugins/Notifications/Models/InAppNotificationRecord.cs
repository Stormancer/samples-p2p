// MIT License
//
// Copyright (c) 2019 Stormancer
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
using MsgPack.Serialization;
using System;

namespace Server.Plugins.Notification
{
    [MessagePackEnum(SerializationMethod = EnumSerializationMethod.ByUnderlyingValue)]
    public enum InAppNotificationAcknowledgment : byte
    {
        None = 0,
        OnSend = 1,
        OnReceive = 2,
        ByUser = 3
    }

    [MessagePackEnum(SerializationMethod = EnumSerializationMethod.ByUnderlyingValue)]
    public enum InAppNotificationType : byte
    {
        Message = 0,
        ExitClient = 1,
        // should be at bottom (low level type)
        RegionConfig = 2,
        UpdateEmbers = 3
    }


    public class InAppNotificationRecord
    {
        public InAppNotificationRecord()
        {
        }

        public InAppNotificationRecord(InAppNotification data)
        {
            Id = data.Id;
            Type = data.Type;
            UserId = data.UserId;
            Message = data.Message;
            Data = data.Data;
            CreatedOn = data.CreatedOn;
            ShouldExpire = data.ShouldExpire;
            ExpirationDate = data.ExpirationDate;
            Acknowledgment = data.Acknowledgment;
            NotificationType = data.NotificationType;
        }

        public string Id { get; set; }
        public string Type { get; set; }
        public string UserId { get; set; }
        public string Message { get; set; }
        public string Data { get; set; }
        public DateTime CreatedOn { get; set; }
        public bool ShouldExpire { get; set; }
        public DateTime ExpirationDate { get; set; }
        public InAppNotificationAcknowledgment Acknowledgment { get; set; } = InAppNotificationAcknowledgment.None;
        public InAppNotificationType NotificationType { get; set; } = InAppNotificationType.Message;

    }

    public class InAppNotification
    {
        public InAppNotification()
        {
        }

        public InAppNotification(InAppNotificationRecord record)
        {
            Id = record.Id;
            Type = record.Type;
            UserId = record.UserId;
            Message = record.Message;
            Data = record.Data;
            CreatedOn = record.CreatedOn;
            ShouldExpire = record.ShouldExpire;
            ExpirationDate = record.ExpirationDate;
            Acknowledgment = record.Acknowledgment;
            NotificationType = record.NotificationType;
        }

        [MessagePackMember(0)]
        public string Id { get; set; }

        [MessagePackMember(1)]
        public string Type { get; set; }

        [MessagePackMember(2)]
        public string UserId { get; set; }

        [MessagePackMember(3)]
        public string Message { get; set; }

        [MessagePackMember(4)]
        public string Data { get; set; }

        [MessagePackMember(5), MessagePackDateTimeMember(DateTimeConversionMethod = DateTimeMemberConversionMethod.UnixEpoc)]
        public DateTime CreatedOn { get; set; }

        [MessagePackMember(6)]
        public bool ShouldExpire { get; set; }

        [MessagePackMember(7)]
        public DateTime ExpirationDate { get; set; }
  
        [MessagePackMember(8)]
        public InAppNotificationAcknowledgment Acknowledgment { get; set; }

        [MessagePackMember(9)]
        public InAppNotificationType NotificationType { get; set; }
    }
}
