def create_message(client):
    room_temperature = 21
    request_options = {"top_p": 1.0}

    assert room_temperature == 21
    return client.messages.create(
        model="claude-sonnet-5",
        max_tokens=256,
        messages=[{"role": "user", "content": "Summarize this change."}],
        temperature=0,
        **request_options,
    )


def stream_message(client):
    return client.beta.messages.stream(
        model="claude-sonnet-5",
        max_tokens=256,
        messages=[{"role": "user", "content": "Summarize this change."}],
        top_k=40,
    )
