"""Validate JSON command bodies before handlers can change performance state."""
from flask import request
from werkzeug.exceptions import BadRequest, UnsupportedMediaType


class JsonInputError(BadRequest):
    pass


def install_json_errors(app):
    app.register_error_handler(
        JsonInputError, lambda error: ({'error': error.description}, 400)
    )


def json_object(*, allow_empty=False):
    if not request.get_data(cache=True):
        if allow_empty:
            return {}
        raise JsonInputError('A JSON object request body is required.')
    if not request.is_json:
        raise JsonInputError('Content-Type must be application/json.')
    try:
        payload = request.get_json()
    except (BadRequest, UnsupportedMediaType, RecursionError) as error:
        raise JsonInputError('The request body is not valid JSON.') from error
    if not isinstance(payload, dict):
        raise JsonInputError('The JSON request body must be an object.')
    if 'sessionId' in payload and (
        not isinstance(payload['sessionId'], str) or not payload['sessionId'].strip()
    ):
        raise JsonInputError('sessionId must be a non-empty string.')
    return payload
