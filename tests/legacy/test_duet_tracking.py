import unittest
from types import SimpleNamespace
from duet_tracking import DuetHandRouter, palm_center
from test_gesture_recognizer import hand


def observation(x, gesture=1, handedness='Left'):
    fingers = [set(), {'index'}, {'index', 'middle'}, {'thumb', 'index', 'middle'},
               {'index', 'middle', 'ring', 'pinky'}, {'thumb', 'index', 'middle', 'ring', 'pinky'}]
    points = hand(fingers[gesture])
    shift = x-palm_center(points)[0]
    return {'landmarks':[SimpleNamespace(x=p.x+shift, y=p.y, z=p.z) for p in points], 'handedness':handedness}


class DuetTests(unittest.TestCase):
    def test_two_independent_poses_and_detection_order(self):
        router = DuetHandRouter()
        left, right = observation(.22, 1, 'Right'), observation(.78, 5, 'Left')
        initial = router.update([left, right], 0)
        result = router.update([right, left], .6)
        self.assertEqual(result['left']['gesture'], 1)
        self.assertEqual(result['right']['gesture'], 5)
        self.assertEqual(result['left']['gestureHold'], 1)
        self.assertEqual(result['right']['gestureHold'], 1)
        self.assertEqual(initial['left']['ownerId'], result['left']['ownerId'])
        self.assertEqual(initial['right']['ownerId'], result['right']['ownerId'])

    def test_either_anatomical_hand_works_in_either_zone(self):
        for handedness in ('Left', 'Right'):
            for side, x in (('left', .2), ('right', .8)):
                for gesture in range(1, 6):
                    with self.subTest(side=side, handedness=handedness, gesture=gesture):
                        router = DuetHandRouter()
                        item = observation(x, gesture, handedness)
                        router.update([item], 1)
                        self.assertEqual(router.update([item], 1.6)[side]['gesture'], gesture)

    def test_center_dead_zone_does_not_acquire_control(self):
        router = DuetHandRouter()
        for x in (.45, .5, .55):
            result = router.update([observation(x)], x)
            self.assertFalse(result['left']['ownerHeld'])
            self.assertFalse(result['right']['ownerHeld'])

    def test_crossing_center_does_not_reassign_ownership(self):
        router = DuetHandRouter()
        first = router.update([observation(.2, 1, 'Left'), observation(.8, 5, 'Right')], 0)
        for index in range(1, 7):
            result = router.update([observation(.2+index*.1, 1, 'Left'), observation(.8-index*.1, 5, 'Right')], index*.05)
        self.assertEqual(result['left']['ownerId'], first['left']['ownerId'])
        self.assertEqual(result['right']['ownerId'], first['right']['ownerId'])
        self.assertEqual(result['left']['handedness'], 'Left')
        self.assertEqual(result['right']['handedness'], 'Right')
        self.assertLessEqual(result['left']['bodyX'], 1)
        self.assertGreaterEqual(result['right']['bodyX'], -1)

    def test_short_loss_preserves_owner_but_not_a_stale_pose(self):
        router = DuetHandRouter()
        first = router.update([observation(.12)], 0)
        lost = router.update([], .8)
        self.assertTrue(lost['left']['ownerHeld'])
        self.assertFalse(lost['left']['handDetected'])
        self.assertEqual(lost['left']['gesture'], 0)
        self.assertEqual(lost['left']['bodyX'], first['left']['bodyX'])
        recovered = router.update([observation(.14)], .9)
        self.assertEqual(recovered['left']['ownerId'], first['left']['ownerId'])
        lost = router.update([], 2)
        self.assertFalse(lost['left']['ownerHeld'])

    def test_another_hand_cannot_steal_a_reserved_zone(self):
        router = DuetHandRouter()
        first = router.update([observation(.2, handedness='Left')], 0)
        second = router.update([observation(.24, 5, 'Right')], .5)
        self.assertEqual(second['left']['ownerId'], first['left']['ownerId'])
        self.assertFalse(second['left']['handDetected'])
        released = router.update([observation(.24, 5, 'Right')], 1.1)
        self.assertTrue(released['left']['handDetected'])
        self.assertNotEqual(released['left']['ownerId'], first['left']['ownerId'])

    def test_movement_uses_each_half_of_the_camera(self):
        router = DuetHandRouter()
        for index in range(30):
            result = router.update([observation(.04, handedness='Left'), observation(.96, handedness='Right')], index/30)
        self.assertLess(result['left']['bodyX'], -.8)
        self.assertGreater(result['right']['bodyX'], .8)


if __name__ == '__main__':
    unittest.main()
