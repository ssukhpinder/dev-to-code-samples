def create_message(client):
    room_temperature = 21

    assert room_temperature == 21
    return client.messages.create(
        model="claude-sonnet-5",
        max_tokens=256,
        messages=[{"role": "user", "content": "Summarize this change."}],
    )


def stream_message(client):
    return client.beta.messages.stream(
        model="claude-sonnet-5",
        max_tokens=256,
        messages=[{"role": "user", "content": "Summarize this change."}],
    )
