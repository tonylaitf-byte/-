import copy,unittest
from types import SimpleNamespace
from duet_mapping_tracking import DuetMappingRouter
from duet_tracking import palm_center
import test_puppet_controls as samples

def observation(x=.2,handedness='Left'):
    points=samples.PuppetControlsTests().make_landmarks()
    offset=x-palm_center(points)[0]
    return {'landmarks':[SimpleNamespace(x=p.x+offset,y=p.y,z=p.z) for p in points],'handedness':handedness}

class MappingTests(unittest.TestCase):
    def ready(self):
        router=DuetMappingRouter(calibration_samples=3)
        hands=[observation(.2,'Right'),observation(.8,'Left')]
        for n in range(3):state=router.update(hands,n/30)
        return router,hands,state

    def test_calibration_is_independent_and_either_hand_is_supported(self):
        for handedness in ('Left','Right'):
            router=DuetMappingRouter(calibration_samples=3)
            left=observation(.2,handedness);right=observation(.8,handedness)
            for n in range(3):state=router.update([left],n/30)
            self.assertTrue(state['left']['controls']['calibrated'])
            self.assertFalse(state['right']['controls']['calibrated'])
            state=router.update([left,right],.1)
            self.assertTrue(state['left']['controls']['calibrated'])
            self.assertLess(state['right']['controls']['calibrationProgress'],1)

    def test_finger_motion_is_routed_to_only_its_puppet(self):
        router,hands,state=self.ready()
        moved=copy.deepcopy(hands);moved[0]['landmarks'][4].y-=.1
        for n in range(1,20):state=router.update(list(reversed(moved)),.1+n/30)
        self.assertGreater(abs(state['left']['controls']['leftHandY']),.1)
        self.assertAlmostEqual(state['right']['controls']['leftHandY'],0,places=3)
        moved=copy.deepcopy(hands);moved[1]['landmarks'][20].y-=.1
        for n in range(1,20):state=router.update(moved,1+n/30)
        self.assertGreater(abs(state['right']['controls']['rightHandY']),.1)

    def test_loss_grace_hold_release_and_new_owner_calibration(self):
        router,hands,first=self.ready()
        moved=copy.deepcopy(hands);moved[0]['landmarks'][4].y-=.1
        state=router.update(moved,.2);held=state['left']['controls']['leftHandY']
        state=router.update([hands[1]],.9)
        self.assertTrue(state['left']['ownerHeld']);self.assertFalse(state['left']['handDetected'])
        self.assertEqual(state['left']['controls']['leftHandY'],held)
        state=router.update([hands[1]],1.3)
        self.assertFalse(state['left']['ownerHeld'])
        state=router.update(hands,1.4)
        self.assertNotEqual(state['left']['ownerId'],first['left']['ownerId'])
        self.assertFalse(state['left']['controls']['calibrated'])

    def test_recalibrate_one_actor_preserves_the_other(self):
        router,hands,first=self.ready();router.recalibrate('left')
        state=router.update(hands,.2)
        self.assertFalse(state['left']['controls']['calibrated'])
        self.assertTrue(state['right']['controls']['calibrated'])
        self.assertEqual(state['right']['ownerId'],first['right']['ownerId'])
        with self.assertRaises(ValueError):router.recalibrate('invalid')

    def test_center_ownership_and_bounded_movement(self):
        router=DuetMappingRouter(calibration_samples=3)
        state=router.update([observation(.5)],0)
        self.assertFalse(state['left']['ownerHeld']);self.assertFalse(state['right']['ownerHeld'])
        router,hands,first=self.ready()
        for n in range(1,11):
            hands=[observation(.2+n*.06,'Right'),observation(.8-n*.06,'Left')]
            state=router.update(hands,.1+n/30)
        self.assertEqual(state['left']['ownerId'],first['left']['ownerId'])
        self.assertEqual(state['right']['ownerId'],first['right']['ownerId'])
        self.assertLessEqual(abs(state['left']['controls']['bodyX']),1)
        self.assertLessEqual(abs(state['right']['controls']['bodyX']),1)

if __name__=='__main__':unittest.main()
